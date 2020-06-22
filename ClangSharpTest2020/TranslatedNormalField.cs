﻿using ClangSharp;
using ClangSharp.Interop;
using System;

namespace ClangSharpTest2020
{
    public sealed class TranslatedNormalField : TranslatedField
    {
        internal FieldDecl Field { get; }

        protected override string AccessModifier { get; }

        private readonly bool IsBitField;

        internal unsafe TranslatedNormalField(TranslatedRecord record, PathogenRecordField* field)
            : base(record, field)
        {
            if (field->Kind != PathogenRecordFieldKind.Normal)
            { throw new ArgumentException("The specified field must be a normal field.", nameof(field)); }

            Field = (FieldDecl)File.FindCursor(field->FieldDeclaration);
            IsBitField = field->IsBitField != 0;

            AccessModifier = Field.Access switch
            {
                CX_CXXAccessSpecifier.CX_CXXPublic => "public",
                CX_CXXAccessSpecifier.CX_CXXProtected => "private", //TODO: Implement protected access
                _ => "private"
            };
        }

        public override void Translate(CodeWriter writer)
        {
            //TODO: Bitfields
            using var _bitfields = writer.DisableScope(IsBitField, File, Context, "Unimplemented translation: Bitfields.");

            // Perform the translation
            base.Translate(writer);
        }
    }
}
