﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;


namespace GetterSetter
{
    [Generator]
    public class AutoPropertyGenerator : ISourceGenerator
    {
        
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization(x => SetDefaultAttribute(x));
            context.RegisterForSyntaxNotifications( () => new SyntaxReceiver() );
        }
        
        private void SetDefaultAttribute(GeneratorPostInitializationContext context)
        {
            // AutoPropertyAttributeのコード本体
            const string AttributeText = @"
using System;
namespace AutoProperty
{
    [AttributeUsage(AttributeTargets.Field,
                    Inherited = false, AllowMultiple = false)]
    sealed class AutoPropAttribute : Attribute
    {
    
        public Type Type { get; set; }
        public AXS AXSType { get; set; }
        
        public AutoPropAttribute(AXS access = AXS.PublicGet)
        {
            AXSType = access;
        }

        public AutoPropAttribute(Type type, AXS access = AXS.PublicGet)
        {
            Type = type;
            AXSType = access;
        }
        

    }

    [Flags]
    internal enum AXS
    {
        PublicGet = 1,
        PublicGetSet = 1 << 1,
        PublicGetPrivateSet = 1 << 2,
        PrivateGet = 1 << 3,
        PrivateGetSet = 1 << 4,
        ProtectedGet = 1 << 5,
        ProtectedGetSet = 1 << 6,
        ProtectedGetPrivateSet = 1 << 7,
        InternalGet = 1 << 8,
        InternalGetSet = 1 << 9,
        InternalGetPrivateSet = 1 << 10,
        ProtectedInternalGet = 1 << 11,
        ProtectedInternalGetSet = 1 << 12,
        ProtectedInternalGetPrivateSet = 1 << 13,
    }
}
";            
            //コンパイル時に参照するアセンブリを追加
            context.AddSource
            (
                "AutoPropAttribute.cs",
                SourceText.From(AttributeText,Encoding.UTF8)
            );
        }

        public void Execute(GeneratorExecutionContext context)
        {
            //Context.SyntaxReceiverというプロパティに格納されているので
            //それを取得する
            var receiver = context.SyntaxReceiver as SyntaxReceiver;
            if (receiver == null) return;
            
            var fieldSymbols = new List<(IFieldSymbol field, ITypeSymbol sourceType , ITypeSymbol targetType , AXS acess)>();

            foreach (var field in receiver.TargetFields)
            {
                var model = context.Compilation.GetSemanticModel(field.field.SyntaxTree);
                foreach (var variable in field.field.Declaration.Variables)
                {
                    var fieldSymbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;

                    var arguments = field.attr.ArgumentList?.Arguments;
                    (IFieldSymbol field, ITypeSymbol sourceType , ITypeSymbol targetType , AXS acess) result =
                        (fieldSymbol, fieldSymbol.Type, fieldSymbol.Type, AXS.PublicGet);
                    if (arguments.HasValue)
                    {
                        foreach (var argument in arguments)
                        {
                            var expr = argument.Expression;
                            
                            if ( expr is TypeOfExpressionSyntax typeOfExpr)
                            {
                                var typeSymbol = model.GetSymbolInfo(typeOfExpr.Type).Symbol as ITypeSymbol;
                                result.targetType = typeSymbol;
                            }
                            //if (argument.NameEquals?.Name.Identifier.Text == "access")
                            else
                            {
                                var parsed = Enum.ToObject(typeof(AXS), model.GetConstantValue(expr).Value);
                                result.acess = (AXS)parsed;
                            }
           
                        }
                    }
                    fieldSymbols.Add(result);
                }
            }
            
            //クラス単位にまとめて、そこからpartialなクラスを生成したいので、
            //クラス名をキーにしてグループ化する
            foreach (var group in fieldSymbols.GroupBy(field=>field.field.ContainingType))
            {
                //classSourceにクラス定義のコードが入る
                var classSource = ProcessClass(group.Key, group.ToList());
                //クラス名.Generated.csという名前でコード生成
                context.AddSource
                    (
                        $"{group.Key.Name}.Generated.cs",
                        SourceText.From(classSource,Encoding.UTF8)
                    );

            }
            
        }
        
        private string ProcessClass(INamedTypeSymbol classSymbol, List<(IFieldSymbol field, ITypeSymbol sourceType , ITypeSymbol targetType , AXS acess)> fieldSymbols)
        {
            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : $"namespace {classSymbol.ContainingNamespace.ToDisplayString()}\n{{\n";
            var classDeclaration = $"public partial class {classSymbol.Name}\n{{\n";

            var builder = new StringBuilder();
            builder.Append(namespaceName);
            builder.Append(classDeclaration);

            foreach (var (field, sourceType, targetType, acess) in fieldSymbols)
            {
                var className = targetType.ToDisplayString();
                var sourceClassName = sourceType.ToDisplayString();
                var propertyName = GetPropertyName(field.Name);
                bool typeIsSame = className == sourceClassName;

                switch (acess)
                {
                    case AXS.PrivateGet:
                    case AXS.PrivateGetSet:
                        builder.Append($@"
    private {className} {propertyName}
    {{
        get
        {{");
                        break;
                    case AXS.PublicGet:
                    case AXS.PublicGetSet:
                    case AXS.PublicGetPrivateSet:
                        builder.Append($@"
    public {className} {propertyName}
    {{
        get
        {{");
                        break;
                    case AXS.ProtectedGet:
                    case AXS.ProtectedGetSet:
                    case AXS.ProtectedGetPrivateSet:
                        builder.Append($@"
    protected {className} {propertyName}
    {{
        get
        {{");
                        break;
                    case AXS.InternalGet:
                    case AXS.InternalGetSet:
                    case AXS.InternalGetPrivateSet:
                        builder.Append($@"
    internal {className} {propertyName}
    {{
        get
        {{");
                        break;
                    case AXS.ProtectedInternalGet:
                    case AXS.ProtectedInternalGetSet:
                    case AXS.ProtectedInternalGetPrivateSet:
                        builder.Append($@"
    protected internal {className} {propertyName}
    {{
        get
        {{");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                
                if (typeIsSame)
                {
                    builder.Append($@"
            return this.{field.Name};
        }}");
                }
                else
                {
                    builder.Append($@"
            return ({className})this.{field.Name};
        }}");
                }
                

                switch (acess)
                {
                    case AXS.PrivateGetSet:
                    case AXS.ProtectedGetSet:
                    case AXS.PublicGetSet:
                    case AXS.InternalGetSet:
                    case AXS.ProtectedInternalGetSet:
                        builder.Append($@"
        set
        {{");
                        if (typeIsSame)
                        {
                        builder.Append($@"
            this.{field.Name} = value;
        }}");
                        }
                        else
                        {
                        builder.Append($@"
            this.{field.Name} = ({sourceClassName})value;
        }}");
                        }

                    break;
                        
                    case AXS.PublicGetPrivateSet:
                    case AXS.ProtectedGetPrivateSet:
                    case AXS.InternalGetPrivateSet:
                    case AXS.ProtectedInternalGetPrivateSet:
                        builder.Append($@"
        private set
        {{");
                        if (typeIsSame)
                        {
                            builder.Append($@"
            this.{field.Name} = value;
        }}");
                        }
                        else
                        {
                            builder.Append($@"
            this.{field.Name} = ({sourceClassName})value;
        }}");
                        }
                        
                        break;
                    

                }

                    builder.Append($@"
    }}
");
            }
            

            builder.Append("}\n"); // Close class
            if (!classSymbol.ContainingNamespace.IsGlobalNamespace)
            {
                builder.Append("}\n"); // Close namespace
            }

            return builder.ToString();
        }

        
        private string GetPropertyName(string fieldName)
        {
            
            // 最初の大文字に変換可能な文字を探す
            for (int i = 0; i < fieldName.Length; i++)
            {
                if (char.IsLower(fieldName[i]))
                {
                    // 大文字に変換して、残りの文字列を結合
                    return char.ToUpper(fieldName[i]) + fieldName.Substring(i + 1);
                }
            }

            // 大文字に変換可能な文字がない場合
            return "NoLetterCanUppercase";
        }
        
        private (bool Implicit ,bool Explicit) CheckConversionInType(ITypeSymbol typeToCheck, ITypeSymbol targetType)
        {
            (bool Implicit ,bool Explicit) result = (false ,false);
                    
            foreach (var member in typeToCheck.GetMembers())
            {
                if (member is IMethodSymbol methodSymbol &&
                    methodSymbol.MethodKind == MethodKind.Conversion &&
                    methodSymbol.ReturnType.Equals(targetType, SymbolEqualityComparer.Default))
                {
                    if(methodSymbol.IsImplicitlyDeclared)
                        result.Implicit = true;
                    else
                        result.Explicit = true;
                }
            }
            
            return result;
        }
        
        

    }

    class SyntaxReceiver : ISyntaxReceiver
    {
        public List<(FieldDeclarationSyntax field, AttributeSyntax attr)> TargetFields { get; } = new List<(FieldDeclarationSyntax field, AttributeSyntax attr)>();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is FieldDeclarationSyntax field)
            {
                foreach (var attributeList in field.AttributeLists)
                {
                    foreach (var attribute in attributeList.Attributes)
                    {
                        // ここで属性の名前をチェックします
                        if (attribute.Name.ToString().EndsWith("AutoPropAttribute") ||
                            attribute.Name.ToString().EndsWith("AutoProp")) // 短縮形も考慮
                        {
                            TargetFields.Add((field,attribute));
                            return; // 一致する属性が見つかったら、他の属性はチェックしない
                        }
                    }
                }
            }
        }
    }
    
    
}