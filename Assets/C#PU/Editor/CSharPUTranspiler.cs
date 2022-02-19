using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using UnityEngine;
using System.IO;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;

namespace CSharpPU.Editor
{
    public class CSharPUTranspiler
    {
        readonly ClassDeclarationSyntax declaration;

        readonly StringBuilder code;

        readonly string filePath;

        readonly Dictionary<string, string> TypeTranslator = new Dictionary<string, string>()
        {
            { "NativeArray", "RWStructuredBuffer" },
            { "NativeList", "AppendStructuredBuffer" },
        };

        readonly Dictionary<string, string> ToReadOnly = new Dictionary<string, string>()
        {
            { "RWStructuredBuffer", "StructuredBuffer" }
        };

        readonly Dictionary<string, string> ModifierTranslator = new Dictionary<string, string>()
        {
            { "ref", "inout" }
        };

        public CSharPUTranspiler (string filePath, ClassDeclarationSyntax classDeclaration)
        {
            this.filePath = filePath;
            declaration = classDeclaration;
            code = new StringBuilder();
        }

    private int m_currentIndentation = 0;

        private void Error(CSharpSyntaxNode node)
        {
            Error(node, $"uses {node.Kind()} (<i>{node.GetType().Name}</i>) which isn't supported");
        }

        private void Error(CSharpSyntaxNode node, string message)
        {
            var line = declaration.SyntaxTree.GetLineSpan(node.Span);
            var path = "Assets" + filePath.Substring(Application.dataPath.Length);
            int lineId = line.StartLinePosition.Line + 1;

            var pathLink = $"<a href=\"{path}\" line=\"{lineId}\">{path}:{lineId}</a>";
            
            Debug.LogError($"C#PU: Shader \"{declaration.Identifier}\" {message}.\n{pathLink}");
        }

        public string GetShaderCode()
        {
            code.Clear();

            var methods = declaration.ChildNodes().OfType<MethodDeclarationSyntax>();
            var fields = declaration.ChildNodes().OfType<FieldDeclarationSyntax>();

            AddKernelPragmas(methods);

            AddFields(fields);

            AddMethods(methods);

            return code.ToString();
        }

        private void AppendExpression(ExpressionSyntax node)
        {
            switch (node.Kind())
            {
                case SyntaxKind.NumericLiteralExpression:
                {
                    string number = node.ToString();

                    
                    code.Append(number.TrimEnd(
                        'f', 'd', 'u', 'm', 'l',
                        'F', 'D', 'U', 'M', 'L'));

                    if (number.EndsWith("f", System.StringComparison.OrdinalIgnoreCase) && !number.Contains('.'))
                    {
                        code.Append(".0");
                    }

                    break;
                }
                case SyntaxKind.SimpleAssignmentExpression:
                {
                    AssignmentExpressionSyntax assignment = node as AssignmentExpressionSyntax;
                    AppendExpression(assignment.Left);
                    code.Append(' ');
                    code.Append(assignment.OperatorToken.ToString());
                    code.Append(' ');
                    AppendExpression(assignment.Right);
                    break;
                }
                case SyntaxKind.IdentifierName:
                {
                    IdentifierNameSyntax name = node as IdentifierNameSyntax;
                    code.Append(name.Identifier);
                    break;
                }
                case SyntaxKind.SimpleMemberAccessExpression:
                {
                    MemberAccessExpressionSyntax access = node as MemberAccessExpressionSyntax;

                    AppendExpression(access.Expression);
                    code.Append(access.OperatorToken.ToString());
                    AppendExpression(access.Name); 

                    break;
                }
                case SyntaxKind.InvocationExpression:
                {
                    InvocationExpressionSyntax invocation = node as InvocationExpressionSyntax;
                    AppendExpression(invocation.Expression);
                    AppendArguments(invocation.ArgumentList);
                    break;
                }
                case SyntaxKind.CastExpression:
                {
                    CastExpressionSyntax @cast = node as CastExpressionSyntax;

                    code.Append('(');
                    code.Append(TranslateType(@cast.Type));
                    code.Append(')');

                    AppendExpression(@cast.Expression);

                    break;
                }
                case SyntaxKind.ElementAccessExpression:
                {
                    ElementAccessExpressionSyntax access = node as ElementAccessExpressionSyntax;
                    AppendExpression(access.Expression);

                    var args = access.ArgumentList.Arguments;

                    if (args.Count > 0)
                    {
                        code.Append('[');
                        for (int i = 0; i < args.Count; ++i)
                        {
                            var arg = args[i];
                            AppendArgument(arg);

                            if (i < args.Count - 1)
                                code.Append(", ");
                        }
                        code.Append(']');
                    }
                    break;
                }
                case SyntaxKind.ParenthesizedExpression:
                {
                    ParenthesizedExpressionSyntax exp = (ParenthesizedExpressionSyntax)node;
                    code.Append('(');
                    AppendExpression(exp.Expression);
                    code.Append(')');
                    break;
                }
                case SyntaxKind.AddExpression:
                case SyntaxKind.MultiplyExpression:
                case SyntaxKind.DivideExpression:
                case SyntaxKind.LeftShiftExpression:
                case SyntaxKind.RightShiftExpression:
                case SyntaxKind.ModuloExpression:

                case SyntaxKind.LessThanOrEqualExpression:
                case SyntaxKind.LessThanExpression:
                case SyntaxKind.GreaterThanExpression:
                case SyntaxKind.GreaterThanOrEqualExpression:
                case SyntaxKind.NotEqualsExpression:
                {
                    BinaryExpressionSyntax exp = (BinaryExpressionSyntax)node;
                    AppendExpression(exp.Left);
                    code.Append(' ');
                    code.Append(exp.OperatorToken);
                    code.Append(' ');
                    AppendExpression(exp.Right);
                    break;
                }
                case SyntaxKind.ObjectCreationExpression:
                {
                    ObjectCreationExpressionSyntax exp = (ObjectCreationExpressionSyntax)node;

                    code.Append(TranslateType(exp.Type));
                    AppendArguments(exp.ArgumentList);

                    break;
                }
                default:
                {
                    Error(node);
                    break;
                }
            }
        }

        private string TranslateType(TypeSyntax node, bool readOnly = false)
        {
            StringBuilder typeBuilder = new StringBuilder();

            switch(node.Kind())
            {
                case SyntaxKind.IdentifierName:
                {
                    IdentifierNameSyntax syntax = node as IdentifierNameSyntax;

                    string typeStr = syntax.ToString();

                    if (TypeTranslator.ContainsKey(typeStr))
                        typeStr = TypeTranslator[typeStr];

                    typeBuilder.Append(typeStr);

                    break;
                }
                case SyntaxKind.QualifiedName:
                {
                    QualifiedNameSyntax syntax = node as QualifiedNameSyntax;

                    string type = TranslateType(syntax.Left, TranslateType(syntax.Right).Equals("ReadOnly"));
                    typeBuilder.Append(type);
                    break;
                }
                case SyntaxKind.PredefinedType:
                {
                    PredefinedTypeSyntax syntax = node as PredefinedTypeSyntax;

                    string typeStr = syntax.ToString();

                    if (TypeTranslator.ContainsKey(typeStr))
                        typeStr = TypeTranslator[typeStr];

                    typeBuilder.Append(typeStr);
                    break;
                }
                case SyntaxKind.GenericName:
                {
                    GenericNameSyntax syntax = node as GenericNameSyntax;
                    var typeStr = syntax.Identifier.ToString();

                    if (TypeTranslator.ContainsKey(typeStr))
                        typeStr = TypeTranslator[typeStr];

                    if (readOnly && ToReadOnly.ContainsKey(typeStr))
                        typeStr = ToReadOnly[typeStr];
                    else if (readOnly) Error(node, $"failed to convert {typeStr} to read only");

                    typeBuilder.Append(typeStr);

                    if (syntax.TypeArgumentList != null)
                    {
                        var args = syntax.TypeArgumentList.Arguments;
                        typeBuilder.Append('<');

                        for (int i = 0; i < args.Count; ++i)
                        {
                            typeBuilder.Append(TranslateType(args[i])); 

                            if (i < args.Count - 1) typeBuilder.Append(' ');
                        }

                        typeBuilder.Append('>');
                    }

                    break;
                }
                default:
                {
                    Error(node);
                    break;
                }
            }
            
            return typeBuilder.ToString();
        }

        private void AddFields(IEnumerable<FieldDeclarationSyntax> fields)
        {
            foreach (var field in fields)
            {
                string type = TranslateType(field.Declaration.Type);

                foreach (var declaration in field.Declaration.Variables)
                {
                    code.Append($"{type} {declaration.Identifier}");

                    if (declaration.Initializer != null)
                    {
                        var expression = declaration.Initializer.Value;
                        code.Append(" = ");
                        AppendExpression(expression);
                    }
                    
                    code.AppendLine($";");
                }
            }
            code.AppendLine();
        }

        private void AddKernelPragmas(IEnumerable<MethodDeclarationSyntax> methods)
        {
            foreach (var method in methods)
            {
                bool isKernel = false;

                foreach (var attributeList in method.AttributeLists)
                {
                    if (isKernel) break;

                    foreach (var attribute in attributeList.Attributes)
                    {
                        if (attribute.Name.ToFullString().Equals("numthreads"))
                        {
                            isKernel = true;
                            break;
                        }
                    }
                }

                if (isKernel) code.AppendLine($"#pragma kernel {method.Identifier.ValueText}");
            }

            code.AppendLine();
        }

        private void AppendArguments(ArgumentListSyntax args)
        {
            if (args != null && args.Arguments != null && args.Arguments.Count > 0)
            {
                code.Append('(');
                var arg = args.Arguments;

                for (int j = 0; j < arg.Count; ++j)
                {
                    AppendArgument(arg[j]);

                    if (j < arg.Count - 1)
                        code.Append(", ");
                }
                code.Append(')');
            }
        }

        private void AppendArgument(ArgumentSyntax argument)
        {
            AppendExpression(argument.Expression);
        }
    
        private void AppendAttributes(AttributeListSyntax attribute)
        {
            if (attribute != null && attribute.Attributes != null)
            {
                for (int i = 0; i < attribute.Attributes.Count; ++i)
                {
                    var at = attribute.Attributes[i];

                    code.Append($"{at.Name}");

                    if (at.ArgumentList != null && at.ArgumentList.Arguments != null && at.ArgumentList.Arguments.Count > 0)
                    {
                        code.Append('(');
                        var args = at.ArgumentList.Arguments;

                        for (int j = 0; j < args.Count; ++j)
                        {
                            AppendExpression(args[j].Expression);

                            if (j < args.Count - 1)
                                code.Append(", ");
                        }
                        code.Append(')');
                    }

                    if (i < attribute.Attributes.Count - 1)
                        code.Append(", ");
                }
            }
        }

        private bool AppendModifier(SyntaxTokenList modifiers)
        {
            if (modifiers.Count > 0)
            {
                for (int i = 0; i < modifiers.Count; ++i)
                {
                    string mod = modifiers[i].ToString();

                    if (ModifierTranslator.ContainsKey(mod))
                        mod = ModifierTranslator[mod];

                    code.Append(mod);

                    if (i < modifiers.Count - 1)
                        code.Append(' ');
                }
                return true;
            }
            return false;
        }

        private void AppendParameter(ParameterSyntax paramater)
        {
            if (AppendModifier(paramater.Modifiers))
                code.Append(' ');

            code.Append($"{TranslateType(paramater.Type)} {paramater.Identifier}");

            if (paramater.AttributeLists != null && paramater.AttributeLists.Count > 0)
            {
                code.Append(" : ");
                foreach(var attributeList in paramater.AttributeLists)
                {
                    AppendAttributes(attributeList);
                }
            }

            if (paramater.Default != null)
            {
                var expression = paramater.Default.Value;
                code.Append(" = ");
                AppendExpression(expression);
            }
        }

        private void AddMethods(IEnumerable<MethodDeclarationSyntax> methods)
        {
            foreach(var method in methods)
            {
                if (method.AttributeLists.Count > 0)
                {
                    foreach(var a in method.AttributeLists)
                    {
                        code.Append('[');
                        AppendAttributes(a);
                        code.AppendLine("]");
                    }
                }

                code.Append($"{TranslateType(method.ReturnType)} {method.Identifier} (");

                if (method.ParameterList != null && method.ParameterList.Parameters.Count > 0)
                {
                    var @params = method.ParameterList.Parameters;
                    for (int i = 0; i < @params.Count; ++i)
                    {
                        AppendParameter(@params[i]);

                        if (i < @params.Count - 1)
                            code.Append(", ");
                    }
                }

                code.AppendLine(")");

                code.AppendLine("{");

                AppendBody(method.Body);

                code.AppendLine("}");
                code.AppendLine();
            }
        }

        private void AppendIndentation()
        {
            code.Append(' ', m_currentIndentation);
        }

        private void AppendIdentedLine(string value)
        {
            code.Append(' ', m_currentIndentation);
            code.AppendLine(value);
        }

        private void AppendStatement(StatementSyntax statement)
        {
            switch(statement.Kind())
            {
                case SyntaxKind.ExpressionStatement:
                {
                    ExpressionStatementSyntax node = (ExpressionStatementSyntax)statement;
                    AppendExpression(node.Expression);
                    code.Append(';'); 
                    break;
                }
                case SyntaxKind.ReturnStatement:
                {
                    ReturnStatementSyntax node = (ReturnStatementSyntax)statement;

                    code.Append("return ");
                    AppendExpression(node.Expression);
                    code.Append(';');

                    break;
                }
                case SyntaxKind.IfStatement:
                {
                    IfStatementSyntax @if = statement as IfStatementSyntax;

                    code.Append("if (");

                    AppendExpression(@if.Condition);

                    code.AppendLine(")");

                    AppendIdentedLine("{");

                    m_currentIndentation += 4;
                    AppendIndentation();
                    AppendStatement(@if.Statement);
                    code.AppendLine();
                    m_currentIndentation -= 4;

                    AppendIdentedLine("}");

                    if (@if.Else != null)
                    {
                        var @else = @if.Else;
                        AppendIdentedLine("else");
                        AppendIdentedLine("{");

                        m_currentIndentation += 4;
                        AppendIndentation();
                        AppendStatement(@else.Statement);
                        code.AppendLine();
                        m_currentIndentation -= 4;

                        AppendIdentedLine("}");
                    }

                    break;
                }
                default: Error(statement); break;
            }
        }

        private void AppendBody(BlockSyntax body)
        {
            m_currentIndentation += 4;

            foreach(var statement in body.Statements)
            {
                code.Append(' ', m_currentIndentation);
                AppendStatement(statement);
                code.AppendLine();
            }

            m_currentIndentation -= 4;
        }
    }
}