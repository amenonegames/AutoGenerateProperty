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
            
            var fieldSymbols = new List<(IFieldSymbol field,TypeSymbolCombination type , ConversionCombination convComb , bool includeSetter)>();

            foreach (var field in receiver.TargetFields)
            {
                var model = context.Compilation.GetSemanticModel(field.SyntaxTree);
                foreach (var variable in field.Declaration.Variables)
                {
                    var fieldSymbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;
                    //フィールド属性からAutoProperty属性があるかを確認
                    var attribute = fieldSymbol.GetAttributes()
                        .FirstOrDefault(attr => attr.AttributeClass.Name == "AutoPropAttribute");

                    if (attribute != null)
                    {
                        // 'Type' プロパティの値を取得
                        TypedConstant typeArgument = attribute.ConstructorArguments[0];
                        
                        ITypeSymbol sorceType = fieldSymbol.Type;
                        ITypeSymbol targetType =
                            typeArgument.IsNull ? fieldSymbol.Type : (ITypeSymbol)typeArgument.Value;

                        TypeSymbolCombination typeSymbols = new TypeSymbolCombination(sorceType, targetType);
                        
                        //Check if field symbol type can cast to typeArgument.Value
                        ConversionCombination convComb = CheckConversion(typeSymbols, context.Compilation);
                        
                        ErrorIfCantConvert(fieldSymbol, convComb, typeSymbols, context);
                        // 'IncludeSetter' プロパティの値を取得（デフォルトは false）
                        bool includeSetter = attribute.ConstructorArguments[1].Value as bool? ?? false;

                        fieldSymbols.Add((fieldSymbol ,typeSymbols ,convComb, includeSetter));
                    }
                }
            }
            
            //クラス単位にまとめて、そこからpartialなクラスを生成したいので、
            //クラス名をキーにしてグループ化する
            foreach (var group in fieldSymbols.GroupBy(field=>field.type.SourceType.ContainingType))
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
        
        ConversionCombination CheckConversion(TypeSymbolCombination typeComb, Compilation compilation)
        {
            if (typeComb.IsSame) 
                return new ConversionCombination(new ConversionType(true,false),new ConversionType(true,false));
            
            // ソース型とターゲット型の両方で暗黙的変換演算子を探索
            return new ConversionCombination(
                CheckConversionInType(typeComb.SourceType, typeComb.TargetType, compilation) ,
                CheckConversionInType(typeComb.TargetType, typeComb.SourceType, compilation));
        }


        ConversionType CheckConversionInType(ITypeSymbol typeToCheck, ITypeSymbol targetType, Compilation compilation)
        {
            ConversionType result = new ConversionType(false,false);
                    
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

            
            
            // 暗黙的変換演算子が見つからなかった
            return result;
        }

        private void ErrorIfCantConvert(IFieldSymbol fieldSymbol, ConversionCombination convComb ,TypeSymbolCombination type,GeneratorExecutionContext context)
        {
            if (convComb.CantConvert)
            {
                // 両方の変換演算子が存在しない場合、コンパイルエラーを生成
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "APG001", // エラーコード
                        "Conversion Not Found", // エラータイトル
                        "No implicit or explicit conversion found from '{0}' to '{1}'", // エラーメッセージ
                        "AutoPropertyGenerator", // カテゴリ
                        DiagnosticSeverity.Error, // 重要度
                        isEnabledByDefault: true),
                    fieldSymbol.Locations[0],
                    fieldSymbol.Type.ToDisplayString(),
                    type.TargetType.ToDisplayString()));
                
            }
        }
        
        private string ProcessClass(INamedTypeSymbol classSymbol, List<(IFieldSymbol field,TypeSymbolCombination type , ConversionCombination convComb , bool includeSetter)> fieldSymbols)
        {
            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace ? "" : $"namespace {classSymbol.ContainingNamespace.ToDisplayString()}\n{{\n";
            var classDeclaration = $"public partial class {classSymbol.Name}\n{{\n";

            var builder = new StringBuilder();
            builder.Append(namespaceName);
            builder.Append(classDeclaration);

            foreach (var ( field , type, convComb,includeSetter) in fieldSymbols)
            {
                var targetClassName = type.TargetType.ToDisplayString();
                var sourceClassName = type.SourceType.ToDisplayString();
                var propertyName = GetPropertyName(field.Name);
                builder.Append($@"
    public {targetClassName} {propertyName}
    {{
        get
        {{
            return this.{field.Name};
        }}");

                if (includeSetter)
                {
                    builder.Append($@"
        set
        {{
            this.{field.Name} = value;
        }}");
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
        
        private struct ConversionType
        {
            public bool Implicit { get; set; }
            public bool Explicit { get; set; }
            
            public ConversionType(bool imp, bool exp)
            {
                Implicit = imp;
                Explicit = exp;
            }
        }

        private struct ConversionCombination
        {
            public ConversionType SorceToTarget { get; set; }
            public ConversionType TargetToSource { get; set; }
            public bool CantConvert => !SorceToTarget.Implicit && !SorceToTarget.Explicit && !TargetToSource.Implicit && !TargetToSource.Explicit;
            
            public ConversionCombination(ConversionType sorceToTarget, ConversionType targetToSource)
            {
                SorceToTarget = sorceToTarget;
                TargetToSource = targetToSource;
            }
        }
        
        private struct TypeSymbolCombination
        {
            public ITypeSymbol SourceType { get; set; }
            public ITypeSymbol TargetType { get; set; }
            public bool IsSame => SourceType.Equals(TargetType, SymbolEqualityComparer.Default);

            public TypeSymbolCombination(ITypeSymbol sourceType, ITypeSymbol targetType)
            {
                SourceType = sourceType;
                TargetType = targetType;
            }
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
