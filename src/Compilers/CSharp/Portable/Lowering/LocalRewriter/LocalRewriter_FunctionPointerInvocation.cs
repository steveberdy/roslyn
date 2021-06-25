﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal sealed partial class LocalRewriter
    {
        public override BoundNode? VisitFunctionPointerInvocation(BoundFunctionPointerInvocation node)
        {
            var rewrittenExpression = VisitExpression(node.InvokedExpression);

            // There are target types so we can have handler conversions, but there are no attributes so contexts cannot
            // be involved.
            AssertNoImplicitInterpolatedStringHandlerConversions(node.Arguments, allowConversionsWithNoContext: true);

            MethodSymbol functionPointer = node.FunctionPointer.Signature;
            var argumentRefKindsOpt = node.ArgumentRefKindsOpt;
            ArrayBuilder<LocalSymbol>? temps = null;
            ImmutableArray<BoundExpression> rewrittenArgs = MakeArguments(
                node.Syntax,
                node.Arguments,
                functionPointer,
                expanded: false,
                argsToParamsOpt: default,
                ref argumentRefKindsOpt,
                ref temps,
                positionsAssignedToTemp: default,
                invokedAsExtensionMethod: false,
                enableCallerInfo: ThreeState.False);

            Debug.Assert(rewrittenExpression != null);
            node = node.Update(rewrittenExpression, rewrittenArgs, argumentRefKindsOpt, node.ResultKind, node.Type);

            if (temps?.Count is null or 0)
            {
                temps?.Free();
                return node;
            }

            return new BoundSequence(node.Syntax, temps.ToImmutableAndFree(), sideEffects: ImmutableArray<BoundExpression>.Empty, node, node.Type);
        }
    }
}
