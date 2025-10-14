using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace YaeAchievement.SourceGeneration;

[Generator(LanguageNames.CSharp)]
public sealed class MinHookAttachGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<AttachInfo>> provider = context.SyntaxProvider.CreateSyntaxProvider(Filter, Transform).Collect();
        context.RegisterSourceOutput(provider, Generate);
    }

    private static bool Filter(SyntaxNode node, CancellationToken token)
    {
        return node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.Text: "MinHook" },
                Name.Identifier.Text: "Attach"
            }
        };
    }

    private static AttachInfo Transform(GeneratorSyntaxContext context, CancellationToken token)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
        SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
        if (args.Count is not 3)
        {
            return null;
        }

        string type = context.SemanticModel.GetTypeInfo(args[0].Expression).Type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        if (string.IsNullOrEmpty(type))
        {
            return null;
        }

        return new()
        {
            MinimallyQualifiedType = type,
        };
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<AttachInfo> infoArray)
    {
        CompilationUnitSyntax unit = CompilationUnit()
            .WithMembers(List<MemberDeclarationSyntax>(
            [
                FileScopedNamespaceDeclaration(ParseName("Yae.Utilities")),
                ClassDeclaration("MinHook")
                    .WithModifiers(TokenList(Token(SyntaxKind.InternalKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.PartialKeyword)))
                    .WithMembers(List(GenerateMethods(infoArray)))
            ]));

        context.AddSource("MinHook.Attach.g.cs", unit.NormalizeWhitespace().ToFullString());
    }

    private static IEnumerable<MemberDeclarationSyntax> GenerateMethods(ImmutableArray<AttachInfo> infoArray)
    {
        foreach (AttachInfo info in infoArray)
        {
            TypeSyntax type = ParseTypeName(info.MinimallyQualifiedType);

            yield return MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier("Attach"))
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.UnsafeKeyword)))
                .WithParameterList(ParameterList(SeparatedList(
                [
                    Parameter(Identifier("origin")).WithType(type),
                    Parameter(Identifier("handler")).WithType(type),
                    Parameter(Identifier("trampoline")).WithType(type).WithModifiers(TokenList(Token(SyntaxKind.OutKeyword)))
                ])))
                .WithBody(Block(List<StatementSyntax>(
                [
                    ExpressionStatement(InvocationExpression(IdentifierName("Attach"))
                        .WithArgumentList(ArgumentList(SeparatedList(
                        [
                            Argument(CastExpression(IdentifierName("nint"), IdentifierName("origin"))),
                            Argument(CastExpression(IdentifierName("nint"), IdentifierName("handler"))),
                            Argument(DeclarationExpression(IdentifierName("nint"), SingleVariableDesignation(Identifier("trampoline1"))))
                                .WithRefKindKeyword(Token(SyntaxKind.OutKeyword))
                        ])))),
                    ExpressionStatement(AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        IdentifierName("trampoline"),
                        CastExpression(type, IdentifierName("trampoline1"))))
                ])));
        }
    }

    private record AttachInfo
    {
        public required string MinimallyQualifiedType { get; init; }
    }
}