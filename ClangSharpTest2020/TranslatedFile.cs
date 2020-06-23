﻿using ClangSharp;
using ClangSharp.Interop;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ClangSharpTest2020
{
    public sealed partial class TranslatedFile : IDeclarationContainer, IDisposable
    {
        public TranslatedLibrary Library { get; }
        TranslatedFile IDeclarationContainer.File => this;

        /// <summary>Declarations for which <see cref="TranslatedDeclaration.CanBeRoot"/> is true.</summary>
        private readonly List<TranslatedDeclaration> IndependentDeclarations = new List<TranslatedDeclaration>();
        /// <summary>Declarations for which <see cref="TranslatedDeclaration.CanBeRoot"/> is false.</summary>
        private readonly List<TranslatedDeclaration> LooseDeclarations = new List<TranslatedDeclaration>();
        public bool IsEmptyTranslation => IndependentDeclarations.Count == 0 && LooseDeclarations.Count == 0;

        private readonly Dictionary<Decl, TranslatedDeclaration> DeclarationLookup = new Dictionary<Decl, TranslatedDeclaration>();

        public string FilePath { get; }
        private readonly TranslationUnit TranslationUnit;

        private readonly List<TranslationDiagnostic> _Diagnostics = new List<TranslationDiagnostic>();
        public ReadOnlyCollection<TranslationDiagnostic> Diagnostics { get; }

        /// <summary>The name of the type which will contain the declarations from <see cref="LooseDeclarations"/>.</summary>
        private string LooseDeclarationsTypeName { get; }

        /// <summary>True if <see cref="Diagnostics"/> contains any diagnostic with <see cref="TranslationDiagnostic.IsError"/> or true.</summary>
        public bool HasErrors { get; private set; }

        internal TranslatedFile(TranslatedLibrary library, CXIndex index, string filePath)
        {
            Library = library;
            FilePath = filePath;
            Diagnostics = _Diagnostics.AsReadOnly();

            // These are the flags used by ClangSharp.PInvokeGenerator, so we're just gonna use them for now.
            CXTranslationUnit_Flags translationFlags =
                CXTranslationUnit_Flags.CXTranslationUnit_IncludeAttributedTypes
            ;

            CXTranslationUnit unitHandle;
            CXErrorCode status = CXTranslationUnit.TryParse(index, FilePath, Library.ClangCommandLineArguments, ReadOnlySpan<CXUnsavedFile>.Empty, translationFlags, out unitHandle);

            if (status != CXErrorCode.CXError_Success)
            {
                Diagnostic(Severity.Fatal, $"Failed to parse source file due to Clang error {status}.");
                return;
            }

            try
            {
                if (unitHandle.NumDiagnostics != 0)
                {
                    for (uint i = 0; i < unitHandle.NumDiagnostics; i++)
                    {
                        using CXDiagnostic diagnostic = unitHandle.GetDiagnostic(i);
                        Diagnostic(diagnostic);
                    }

                    if (HasErrors)
                    {
                        Diagnostic(Severity.Fatal, "Aborting translation due to previous errors.");
                        unitHandle.Dispose();
                        return;
                    }
                }
            }
            catch
            {
                unitHandle.Dispose();
                throw;
            }

            // Create the translation unit
            TranslationUnit = TranslationUnit.GetOrCreate(unitHandle);

            // Process the translation unit
            ProcessCursor(this, TranslationUnit.TranslationUnitDecl);

            // Associate loose declarations (IE: global functions and variables) to a record matching our file name if we have one.
            LooseDeclarationsTypeName = Path.GetFileNameWithoutExtension(FilePath);
            if (LooseDeclarations.Count > 0)
            {
                //TODO: This would be problematic for enums which are named LooseDeclarationsTypeName since we'd double-write the file.
                TranslatedRecord looseDeclarationsTarget = IndependentDeclarations.OfType<TranslatedRecord>().FirstOrDefault(r => r.TranslatedName == LooseDeclarationsTypeName);
                if (looseDeclarationsTarget is object)
                {
                    while (LooseDeclarations.Count > 0)
                    { LooseDeclarations[0].Parent = looseDeclarationsTarget; }
                }
            }

            // Note if this file didn't translate into anything
            if (IsEmptyTranslation)
            { Diagnostic(Severity.Note, TranslationUnit.TranslationUnitDecl, "File did not result in anything to be translated."); }
        }

        void IDeclarationContainer.AddDeclaration(TranslatedDeclaration declaration)
        {
            Debug.Assert(ReferenceEquals(declaration.Parent, this));

            if (declaration.CanBeRoot)
            { IndependentDeclarations.Add(declaration); }
            else
            { LooseDeclarations.Add(declaration); }
        }

        void IDeclarationContainer.RemoveDeclaration(TranslatedDeclaration declaration)
        {
            Debug.Assert(ReferenceEquals(declaration.Parent, this));
            bool removed;

            if (declaration.CanBeRoot)
            { removed = IndependentDeclarations.Remove(declaration); }
            else
            { removed = LooseDeclarations.Remove(declaration); }

            Debug.Assert(removed);
        }

        internal void AddDeclarationAssociation(Decl declaration, TranslatedDeclaration translatedDeclaration)
        {
            if (DeclarationLookup.TryGetValue(declaration, out TranslatedDeclaration otherDeclaration))
            {
                Diagnostic
                (
                    Severity.Error,
                    declaration,
                    $"More than one translation corresponds to {declaration.CursorKindDetailed()} '{declaration.Spelling}' (Newest: {translatedDeclaration.GetType().Name}, Other: {otherDeclaration.GetType().Name})"
                );
                return;
            }

            DeclarationLookup.Add(declaration, translatedDeclaration);
        }

        internal void RemoveDeclarationAssociation(Decl declaration, TranslatedDeclaration translatedDeclaration)
        {
            if (DeclarationLookup.TryGetValue(declaration, out TranslatedDeclaration otherDeclaration) && !ReferenceEquals(otherDeclaration, translatedDeclaration))
            {
                Diagnostic
                (
                    Severity.Error,
                    declaration,
                    $"Tried to remove association between {declaration.CursorKindDetailed()} '{declaration.Spelling}' and a {translatedDeclaration.GetType().Name}, but it's associated with a (different) {otherDeclaration.GetType().Name}"
                );
                return;
            }

            bool removed = DeclarationLookup.Remove(declaration, out otherDeclaration);
            Debug.Assert(removed && ReferenceEquals(translatedDeclaration, otherDeclaration));
        }

        public IEnumerator<TranslatedDeclaration> GetEnumerator()
            => IndependentDeclarations.Union(LooseDeclarations).GetEnumerator();

        public void Validate()
        {
            foreach (TranslatedDeclaration declaration in this)
            { declaration.Validate(); }
        }

        private void TranslateLooseDeclarations(CodeWriter writer)
        {
            // If there are no loose declarations, there's nothing to do.
            if (LooseDeclarations.Count == 0)
            { return; }

            // Write out a static class containing all of the loose declarations
            writer.EnsureSeparation();
            writer.WriteLine($"public static unsafe partial class {LooseDeclarationsTypeName}");
            using (writer.Block())
            {
                foreach (TranslatedDeclaration declaration in LooseDeclarations)
                { declaration.Translate(writer); }
            }
        }

        public void Translate()
        {
            // Translate loose declarations
            if (LooseDeclarations.Count > 0)
            {
                using CodeWriter writer = new CodeWriter();
                TranslateLooseDeclarations(writer);
                writer.WriteOut($"{LooseDeclarationsTypeName}.cs");
            }

            // Translate independent declarations
            foreach (TranslatedDeclaration declaration in IndependentDeclarations)
            {
                using CodeWriter writer = new CodeWriter();
                declaration.Translate(writer);
                writer.WriteOut($"{declaration.TranslatedName}.cs");
            }
        }

        public void Translate(CodeWriter writer)
        {
            // Translate loose declarations
            TranslateLooseDeclarations(writer);

            // Translate independent declarations
            foreach (TranslatedDeclaration declaration in IndependentDeclarations)
            { declaration.Translate(writer); }
        }

        private void Diagnostic(in TranslationDiagnostic diagnostic)
        {
            _Diagnostics.Add(diagnostic);

            if (diagnostic.IsError)
            { HasErrors = true; }

            // Send the diagnostic to the library
            Library.Diagnostic(diagnostic);
        }

        internal void Diagnostic(Severity severity, SourceLocation location, string message)
            => Diagnostic(new TranslationDiagnostic(this, location, severity, message));

        internal void Diagnostic(Severity severity, string message)
            => Diagnostic(severity, new SourceLocation(FilePath), message);

        private void Diagnostic(CXDiagnostic clangDiagnostic)
            => Diagnostic(new TranslationDiagnostic(this, clangDiagnostic));

        internal void Diagnostic(Severity severity, Cursor associatedCursor, string message)
            => Diagnostic(severity, new SourceLocation(associatedCursor.Extent.Start), message);

        internal void Diagnostic(Severity severity, CXCursor associatedCursor, string message)
            => Diagnostic(severity, new SourceLocation(associatedCursor.Extent.Start), message);

        internal void ProcessCursorChildren(IDeclarationContainer container, Cursor cursor)
        {
            foreach (Cursor child in cursor.CursorChildren)
            { ProcessCursor(container, child); }
        }

        internal void ProcessCursor(IDeclarationContainer container, Cursor cursor)
        {
            // Skip cursors outside of the specific file being processed
            if (!cursor.IsFromMainFile())
            { return; }

            //---------------------------------------------------------------------------------------------------------
            // Skip cursors which explicitly do not have translation implemented.
            // This needs to happen first in case some of these checks overlap with cursors which are translated.
            // (For instance, class template specializatiosn are records.)
            //---------------------------------------------------------------------------------------------------------
            if (IsExplicitlyUnsupported(cursor))
            {
                Diagnostic(Severity.Ignored, cursor, $"{cursor.CursorKindDetailed()} aren't supported yet.");
                return;
            }

            //---------------------------------------------------------------------------------------------------------
            // Cursors which do not have a direct impact on the output
            // (These cursors are usually just containers for other cursors or the information
            //  they provide is already available on the cursors which they affect.)
            //---------------------------------------------------------------------------------------------------------

            // For translation units, just process all the children
            if (cursor is TranslationUnitDecl)
            {
                Debug.Assert(container is TranslatedFile, "Translation units should only occur within the root declaration container.");
                ProcessCursorChildren(container, cursor);
                return;
            }

            // Ignore linkage specification (IE: `exern "C"`)
            if (cursor.Handle.DeclKind == CX_DeclKind.CX_DeclKind_LinkageSpec)
            {
                ProcessCursorChildren(container, cursor);
                return;
            }

            // Ignore unimportant (to us) attributes on declarations
            if (cursor is Attr attribute)
            {
                switch (attribute.Kind)
                {
                    case CX_AttrKind.CX_AttrKind_DLLExport:
                    case CX_AttrKind.CX_AttrKind_DLLImport:
                    case CX_AttrKind.CX_AttrKind_Aligned:
                        break;
                    default:
                        Diagnostic(Severity.Warning, attribute, $"Attribute of unrecognized kind: {attribute.Kind}");
                        break;
                }

                return;
            }

            // Namespace using directives do not impact the output
            if (cursor is UsingDirectiveDecl)
            { return; }

            // Namespace aliases do not impact the output
            if (cursor is NamespaceAliasDecl)
            { return; }

            // Friend declarations don't really mean anything to C#
            // They're usually implementation details anyway.
            if (cursor is FriendDecl)
            { return; }

            // Base specifiers are discovered by the record layout
            if (cursor is CXXBaseSpecifier)
            { return; }

            // Access specifiers are discovered by inspecting the member directly
            if (cursor is AccessSpecDecl)
            { return; }

            //---------------------------------------------------------------------------------------------------------
            // Cursors which only affect the context
            //---------------------------------------------------------------------------------------------------------

            // Namespaces
            if (cursor is NamespaceDecl namespaceDeclaration)
            {
                Debug.Assert(container is TranslatedFile, "Namespaces should only occur within the root declaration container.");
                ProcessCursorChildren(container, cursor);
                return;
            }

            //---------------------------------------------------------------------------------------------------------
            // Records and loose functions
            //---------------------------------------------------------------------------------------------------------

            // Handle records (classes, structs, and unions)
            if (cursor is RecordDecl record)
            {
                // Ignore forward-declarations
                if (!record.Handle.IsDefinition)
                { return; }

                new TranslatedRecord(container, record);
                return;
            }

            // Handle enums
            if (cursor is EnumDecl enumDeclaration)
            {
                new TranslatedEnum(container, enumDeclaration);
                return;
            }

            // Handle functions and methods
            if (cursor is FunctionDecl function)
            {
                new TranslatedFunction(container, function);
                return;
            }

            // Handle fields
            // This method is not meant to handle fields (they are enumerated by TranslatedRecord when querying the record layout.)
            if (cursor is FieldDecl field)
            {
                Diagnostic(Severity.Warning, field, "Field declaration processed outside of record.");
                return;
            }

            // Handle static fields and globals
            //TODO: Constants need special treatment here.
            if (cursor is VarDecl variable)
            {
                new TranslatedStaticField(container, variable);
                return;
            }

            //---------------------------------------------------------------------------------------------------------
            // Failure
            //---------------------------------------------------------------------------------------------------------

            // If we got this far, we didn't know how to process the cursor
            // At one point we processed the children of the cursor anyway, but this can lead to confusing behavior when the skipped cursor provided meaningful context.
            Diagnostic(Severity.Warning, cursor, $"Not sure how to process cursor of type {cursor.CursorKindDetailed()}.");
        }

        private static bool IsExplicitlyUnsupported(Cursor cursor)
        {
            // Ignore template specializations
            if (cursor is ClassTemplateSpecializationDecl)
            { return true; }

            // Ignore templates
            if (cursor is TemplateDecl)
            { return true; }

            // Ignore typedefs
            // Typedefs will probably almost always have to be a special case.
            // Sometimes they aren't very meaningful to the translation, and sometimes they have a large impact on how the API is used.
            if (cursor is TypedefDecl)
            { return true; }

            // If we got this far, the cursor might be supported
            return false;
        }

        public Cursor FindCursor(CXCursor cursorHandle)
        {
            if (cursorHandle.IsNull)
            {
                Diagnostic(Severity.Warning, $"Someone tried to get the Cursor for a null handle.");
                return null;
            }

            return TranslationUnit.GetOrCreate(cursorHandle);
        }

        internal string GetNameForUnnamed(string category)
            // Names of declarations at the file level should be library-unique, so it names unnamed things.
            => Library.GetNameForUnnamed(category);

        string IDeclarationContainer.GetNameForUnnamed(string category)
            => GetNameForUnnamed(category);

        public void Dispose()
            => TranslationUnit?.Dispose();
    }
}
