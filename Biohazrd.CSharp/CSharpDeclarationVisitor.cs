﻿namespace Biohazrd.CSharp
{
    public abstract class CSharpDeclarationVisitor : DeclarationVisitor
    {
        protected override void Visit(VisitorContext context, TranslatedDeclaration declaration)
        {
            switch (declaration)
            {
                case ConstantArrayTypeDeclaration constantArrayTypeDeclaration:
                    VisitConstantArrayTypeDeclaration(context, constantArrayTypeDeclaration);
                    return;
                case SynthesizedLooseDeclarationsType synthesizedLooseDeclarationsType:
                    VisitSynthesizedLooseDeclarationsType(context, synthesizedLooseDeclarationsType);
                    return;
                default:
                    base.Visit(context, declaration);
                    return;
            }
        }

        protected virtual void VisitConstantArrayTypeDeclaration(VisitorContext context, ConstantArrayTypeDeclaration declaration)
            => VisitDeclaration(context, declaration);

        protected virtual void VisitSynthesizedLooseDeclarationsType(VisitorContext context, SynthesizedLooseDeclarationsType declaration)
            => VisitDeclaration(context, declaration);
    }
}