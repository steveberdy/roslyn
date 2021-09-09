﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.CSharp.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Shared.Collections;
using Microsoft.CodeAnalysis.Shared.Extensions;
using static Microsoft.CodeAnalysis.CodeGeneration.CodeGenerationHelpers;
using static Microsoft.CodeAnalysis.CSharp.CodeGeneration.CSharpCodeGenerationHelpers;

namespace Microsoft.CodeAnalysis.CSharp.CodeGeneration
{
    internal static class OperatorGenerator
    {
        internal static TypeDeclarationSyntax AddOperatorTo(
            TypeDeclarationSyntax destination,
            IMethodSymbol method,
            CodeGenerationOptions options,
            IList<bool> availableIndices)
        {
            var methodDeclaration = GenerateOperatorDeclaration(
                method, options, destination?.SyntaxTree.Options ?? options.ParseOptions);

            var members = Insert(destination.Members, methodDeclaration, options, availableIndices, after: LastOperator);

            return AddMembersTo(destination, members);
        }

        internal static OperatorDeclarationSyntax GenerateOperatorDeclaration(
            IMethodSymbol method,
            CodeGenerationOptions options,
            ParseOptions parseOptions)
        {
            var reusableSyntax = GetReuseableSyntaxNodeForSymbol<OperatorDeclarationSyntax>(method, options);
            if (reusableSyntax != null)
            {
                return RemoveLeadingDirectiveTrivia(reusableSyntax);
            }

            var declaration = GenerateOperatorDeclarationWorker(method, options, parseOptions);
            declaration = UseExpressionBodyIfDesired(options, declaration, parseOptions);

            return AddAnnotationsTo(method,
                ConditionallyAddDocumentationCommentTo(declaration, method, options));
        }

        private static OperatorDeclarationSyntax UseExpressionBodyIfDesired(
            CodeGenerationOptions options, OperatorDeclarationSyntax declaration, ParseOptions parseOptions)
        {
            if (declaration.ExpressionBody == null)
            {
                var expressionBodyPreference = options.Options.GetOption(CSharpCodeStyleOptions.PreferExpressionBodiedOperators).Value;
                if (declaration.Body.TryConvertToArrowExpressionBody(
                        declaration.Kind(), parseOptions, expressionBodyPreference,
                        out var expressionBody, out var semicolonToken))
                {
                    return declaration.WithBody(null)
                                      .WithExpressionBody(expressionBody)
                                      .WithSemicolonToken(semicolonToken);
                }
            }

            return declaration;
        }

        private static OperatorDeclarationSyntax GenerateOperatorDeclarationWorker(
            IMethodSymbol method,
            CodeGenerationOptions options,
            ParseOptions parseOptions)
        {
            var hasNoBody = !options.GenerateMethodBodies || method.IsExtern || method.IsAbstract;

            var operatorSyntaxKind = SyntaxFacts.GetOperatorKind(method.MetadataName);
            if (operatorSyntaxKind == SyntaxKind.None)
            {
                throw new ArgumentException(string.Format(WorkspacesResources.Cannot_generate_code_for_unsupported_operator_0, method.Name), nameof(method));
            }

            var operatorToken = SyntaxFactory.Token(operatorSyntaxKind);

            var operatorDecl = SyntaxFactory.OperatorDeclaration(
                attributeLists: AttributeGenerator.GenerateAttributeLists(method.GetAttributes(), options),
                modifiers: GenerateModifiers(method),
                returnType: method.ReturnType.GenerateTypeSyntax(),
                explicitInterfaceSpecifier: GenerateExplicitInterfaceSpecifier(method.ExplicitInterfaceImplementations),
                operatorKeyword: SyntaxFactory.Token(SyntaxKind.OperatorKeyword),
                operatorToken: operatorToken,
                parameterList: ParameterGenerator.GenerateParameterList(method.Parameters, isExplicit: false, options: options),
                body: hasNoBody ? null : StatementGenerator.GenerateBlock(method),
                expressionBody: null,
                semicolonToken: hasNoBody ? SyntaxFactory.Token(SyntaxKind.SemicolonToken) : new SyntaxToken());

            operatorDecl = UseExpressionBodyIfDesired(options, operatorDecl, parseOptions);
            return operatorDecl;
        }

        private static SyntaxTokenList GenerateModifiers(IMethodSymbol method)
        {
            using var tokens = TemporaryArray<SyntaxToken>.Empty;

            if (method.ExplicitInterfaceImplementations.Length == 0)
            {
                tokens.Add(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
            }

            tokens.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword));

            if (method.IsAbstract)
            {
                tokens.Add(SyntaxFactory.Token(SyntaxKind.AbstractKeyword));
            }

            return tokens.ToImmutableAndClear().ToSyntaxTokenList();
        }
    }
}
