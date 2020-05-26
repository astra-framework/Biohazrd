﻿using ClangSharp;
using ClangSharp.Interop;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClangType = ClangSharp.Type;

namespace ClangSharpTest2020
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, ImportResolver);

            DoTest();
            Console.WriteLine();
            Console.WriteLine("Done.");
            //Console.ReadLine();
        }

        private static IntPtr ImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == "libclang.dll")
            { return NativeLibrary.Load(@"C:\Scratch\llvm-project\build\Release\bin\libclang.dll"); }

            return IntPtr.Zero;
        }

        private static void DoTest()
        {
            const string sourceFilePath = @"C:\Development\Playground\CppWrappingInlineMaybe\CppWrappingInlineMaybe\Source.h";

            string[] clangCommandLineArgs =
            {
                "--language=c++",
                "--std=c++17",
                "-Wno-pragma-once-outside-header", // Since we might be parsing headers, this warning will be irrelevant.
                //"--target=x86_64-pc-linux",
            };

            // These are the flags used by ClangSharp.PInvokeGenerator, so we're just gonna use them for now.
            CXTranslationUnit_Flags translationFlags =
                CXTranslationUnit_Flags.CXTranslationUnit_IncludeAttributedTypes |
                CXTranslationUnit_Flags.CXTranslationUnit_VisitImplicitAttributes
            ;

            CXIndex index = CXIndex.Create(displayDiagnostics: true);
            CXTranslationUnit unitHandle;
            CXErrorCode status = CXTranslationUnit.TryParse(index, sourceFilePath, clangCommandLineArgs, ReadOnlySpan<CXUnsavedFile>.Empty, translationFlags, out unitHandle);

            if (status != CXErrorCode.CXError_Success)
            {
                Console.Error.WriteLine($"Failed to parse due to {status}.");
                return;
            }

            if (unitHandle.NumDiagnostics != 0)
            {
                bool hasErrors = false;
                Console.Error.WriteLine("Compilation diagnostics:");
                for (uint i = 0; i < unitHandle.NumDiagnostics; i++)
                {
                    using CXDiagnostic diagnostic = unitHandle.GetDiagnostic(i);
                    Console.WriteLine($"    {diagnostic.Severity}: {diagnostic.Format(CXDiagnostic.DefaultDisplayOptions)}");

                    if (diagnostic.Severity == CXDiagnosticSeverity.CXDiagnostic_Error || diagnostic.Severity == CXDiagnosticSeverity.CXDiagnostic_Fatal)
                    { hasErrors = true; }
                }

                if (hasErrors)
                {
                    Console.Error.WriteLine("Aborting due to previous errors.");
                    return;
                }
            }

            using TranslationUnit unit = TranslationUnit.GetOrCreate(unitHandle);
            //new ClangWalker().VisitTranslationUnit(unit.TranslationUnitDecl);
            using var writer = new StreamWriter("Output.txt");
            Writer = writer;
            Dump(unit.TranslationUnitDecl);
        }

        private static StreamWriter Writer;

        private static void Dump(Cursor cursor)
        {
            // Skip cursors which come from included files
            // (Can also skip ones from system files. Unclear how Clang determines "system header" -- Might just be <> headers?)
            // For some reason the first declaration in a file will only have its end marked as being from the main file.
            if (!cursor.Extent.Start.IsFromMainFile && !cursor.Extent.End.IsFromMainFile)
            {
                return;
            }

            // Some types of cursors are never relevant
            bool skip = false;

            if (cursor is AccessSpecDecl)
            { skip = true; }

            if (skip)
            {
                if (cursor.CursorChildren.Count > 0)
                { WriteLine("THE FOLLOWING CURSOR WAS GONNA BE SKIPPED BUT IT HAS CHILDREN!"); }
                else
                { return; }
            }

            string extra = "";
            {
                string mangling = cursor.Handle.Mangling.ToString();
                if (!string.IsNullOrEmpty(mangling))
                {
                    extra += $" Mangled={mangling}";
                }

                if (cursor is FunctionDecl function)
                {
                    if (function.IsInlined)
                    { extra += " INLINE"; }
                }

                if (cursor is RecordDecl record)
                {
                    ClangType type = record.TypeForDecl;

                    extra += $" {type.Handle.SizeOf} bytes";

                    if (type.Handle.IsPODType)
                    { extra += " <POD>"; }
                }

#if false
                if (cursor.Extent.Start.IsFromMainFile)
                { extra += " MAIN"; }
                else if (cursor.Extent.End.IsFromMainFile)
                { extra += " MAIN(END)"; }

                if (cursor.Extent.Start.IsInSystemHeader)
                { extra += " SYS"; }
                else if (cursor.Extent.End.IsInSystemHeader)
                { extra += " SYS(END)"; }
#endif
            }

            string kind = cursor.CursorKindSpellingSafe();
            //kind = cursor.GetType().Name;

            WriteLine($"{kind} {cursor.Handle.DeclKind} - {cursor.Spelling}{extra}");

            // Clang seems to have a basic understanding of Doxygen comments.
            // This seems to associate the comment as appropriate for prefix and postfix documentation. Pretty neat!
            string commentText = clang.Cursor_getRawCommentText(cursor.Handle).ToString();
            if (!String.IsNullOrEmpty(commentText))
            { WriteLine(commentText); }

#if false
            if (cursor.Extent.Start.IsFromMainFile != cursor.Extent.End.IsFromMainFile)
            {
                WriteLine("--------------");
                WriteLine("Start and end location do not agree whether this cursor is in the main file!");
                WriteLine($"Start: {cursor.Extent.Start}");
                WriteLine($"  End: {cursor.Extent.End}");
                WriteLine("--------------");
            }
#endif

            // For records, print the layout
            // Helpful: https://github.com/joshpeterson/layout
            bool skipFields = false;
            {
                if (cursor is RecordDecl record)
                {
                    skipFields = true;
                    bool wroteField = false;

                    foreach (Cursor child in cursor.CursorChildren)
                    {
                        if (child.CursorKind != CXCursorKind.CXCursor_FieldDecl)
                        { continue; }

                        if (!wroteField)
                        {
                            wroteField = true;
                            WriteLine("----------------------------------------------------------------------------");
                        }

                        FieldDecl field = (FieldDecl)child;
                        WriteLine($"{field.Type.AsString} {field.Name} @ {field.Handle.OffsetOfField / 8} for {field.Type.Handle.SizeOf}");
                    }


                    // Dump the layout using PathogenLayoutExtensions
                    WriteLine("----------------------------------------------------------------------------");
                    DumpLayoutWithPathogenExtensions(record);
                    WriteLine("----------------------------------------------------------------------------");
                }
            }

            Cursor cursorToIgnore = null;
            {
                if (cursor is FunctionDecl function)
                { cursorToIgnore = function.Body; }
                else if (cursor is FieldDecl && cursor.CursorChildren.Count == 1)
                { cursorToIgnore = cursor.CursorChildren[0]; }
            }

            Indent();
            foreach (Cursor child in cursor.CursorChildren)
            {
                if (child == cursorToIgnore)
                { continue; }

                if (skipFields && child is FieldDecl)
                { continue; }

                Dump(child);
            }
            Unindent();
        }

        private static unsafe void DumpLayoutWithPathogenExtensions(RecordDecl record)
        {
            PathogenRecordLayout* layout = null;

            try
            {
                layout = PathogenExtensions.pathogen_GetRecordLayout(record.Handle);

                // Count the number of fields
                int fieldCount = 0;
                for (PathogenRecordField* field = layout->FirstField; field != null; field = field->NextField)
                { fieldCount++; }

                WriteLine($"          Field count: {fieldCount}");
                WriteLine($"                 Size: {layout->Size} bytes");
                WriteLine($"            Alignment: {layout->Alignment} bytes");
                WriteLine($"        Is C++ record: {(layout->IsCppRecord != 0 ? "Yes" : "No")}");

                if (layout->IsCppRecord != 0)
                {
                    WriteLine($"     Non-virtual size: {layout->NonVirtualSize}");
                    WriteLine($"Non-virtual alignment: {layout->NonVirtualAlignment}");
                }

                for (PathogenRecordField* field = layout->FirstField; field != null; field = field->NextField)
                {
                    string fieldLine = $"[{field->Offset}]";

                    if (field->Kind != PathogenRecordFieldKind.Normal)
                    { fieldLine += $" {field->Kind}"; }

                    fieldLine += $" {field->Type} {field->Name.CString}";

#if false
                    if (field->Kind == PathogenRecordFieldKind.Normal)
                    { fieldLine += $" (FieldDeclaration = {field->FieldDeclaration})"; }
#endif

                    if (field->IsPrimaryBase != 0)
                    { fieldLine += " (PRIMARY)"; }

                    WriteLine(fieldLine);
                }

                // Write out the VTable(s)
                int vTableIndex = 0;
                for (PathogenVTable* vTable = layout->FirstVTable; vTable != null; vTable = vTable->NextVTable)
                {
                    WriteLine($"------- VTABLE {vTableIndex} -------");

                    int i = 0;
                    foreach (PathogenVTableEntry entry in vTable->Entries)
                    {
                        string line = $"[{i}] {entry.Kind}";

                        switch (entry.Kind)
                        {
                            case PathogenVTableEntryKind.VCallOffset:
                            case PathogenVTableEntryKind.VBaseOffset:
                            case PathogenVTableEntryKind.OffsetToTop:
                                line += $" {entry.Offset}";
                                break;
                            case PathogenVTableEntryKind.RTTI:
                                line += $" {entry.RttiType.DisplayName}";
                                break;
                            case PathogenVTableEntryKind.FunctionPointer:
                            case PathogenVTableEntryKind.CompleteDestructorPointer:
                            case PathogenVTableEntryKind.DeletingDestructorPointer:
                            case PathogenVTableEntryKind.UnusedFunctionPointer:
                                line += $" {entry.MethodDeclaration.DisplayName}";
                                break;
                        }

                        WriteLine(line);
                        i++;
                    }

                    vTableIndex++;
                }
            }
            finally
            {
                if (layout != null)
                { PathogenExtensions.pathogen_DeleteRecordLayout(layout); }
            }
        }

        private static int IndentLevel = 0;

        private static void Indent()
            => IndentLevel++;

        private static void Unindent()
            => IndentLevel--;

        private static void WriteLine(string message)
        {
            for (int i = 0; i < IndentLevel; i++)
            { Writer.Write("  "); }

            Writer.WriteLine(message);
        }
    }
}
