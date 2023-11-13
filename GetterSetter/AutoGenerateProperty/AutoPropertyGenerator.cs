using System;
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
        public bool IncludeSetter { get; set; }
        public AutoPropAttribute()
        {
        }

        public AutoPropAttribute(Type type, bool includeSetter = false)
        {
            Type = type;
            IncludeSetter = includeSetter;
        }
        
        public AutoPropAttribute(bool includeSetter = false)
        {
            IncludeSetter = includeSetter;
        }
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
            
            var fieldSymbols = new List<(IFieldSymbol field, ITypeSymbol sourceType , ITypeSymbol targetType , bool includeSetter)>();

            foreach (var field in receiver.TargetFields)
            {
                var model = context.Compilation.GetSemanticModel(field.SyntaxTree);
                foreach (var variable in field.Declaration.Variables)
                {
                    var fieldSymbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;
                    //フィールド属性からAutoProperty属性があるかを確認
                    var attribute  = fieldSymbol.GetAttributes()
                        .FirstOrDefault(attr => attr.AttributeClass.Name == "AutoPropAttribute");
                    
                    if (attribute != null)
                    {
                        // 'Type' プロパティの値を取得
                        TypedConstant typeArgument = attribute.ConstructorArguments
                            .FirstOrDefault(x=>x.Type != null && (x.Type != null && x.Type.Name == "type" || x.Type.Name == "Type"));
                        ITypeSymbol sourceType = fieldSymbol.Type;
                        ITypeSymbol targetType = typeArgument.IsNull ? fieldSymbol.Type : (ITypeSymbol)typeArgument.Value;

                        // 'IncludeSetter' プロパティの値を取得（デフォルトは false）
                        bool includeSetter = attribute.ConstructorArguments
                            .FirstOrDefault(x=>x.Type != null && x.Type.ToDisplayString() == "bool").Value as bool? ?? false;

                        fieldSymbols.Add((fieldSymbol,sourceType, targetType, includeSetter));
                    }
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
        
        private string ProcessClass(INamedTypeSymbol classSymbol, List<(IFieldSymbol field, ITypeSymbol sourceType , ITypeSymbol targetType , bool includeSetter)> fieldSymbols)
        {
            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : $"namespace {classSymbol.ContainingNamespace.ToDisplayString()}\n{{\n";
            var classDeclaration = $"public partial class {classSymbol.Name}\n{{\n";

            var builder = new StringBuilder();
            builder.Append(namespaceName);
            builder.Append(classDeclaration);

            foreach (var (field, sourceType, targetType, includeSetter) in fieldSymbols)
            {
                var className = targetType.ToDisplayString();
                var sourceClassName = sourceType.ToDisplayString();
                var propertyName = GetPropertyName(field.Name);
                bool typeIsSame = className == sourceClassName;
                
                builder.Append($@"
    public {className} {propertyName}
    {{
        get
        {{");
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
                

                if (includeSetter)
                {
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

                    builder.Append($@"
    }}
");
                }
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
        public List<FieldDeclarationSyntax> TargetFields { get; } = new List<FieldDeclarationSyntax>();

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
                            TargetFields.Add(field);
                            return; // 一致する属性が見つかったら、他の属性はチェックしない
                        }
                    }
                }
            }
        }
    }
    
    
}