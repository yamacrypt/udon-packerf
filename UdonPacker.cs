#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography;
using System.Linq;


namespace UdonPacker{
public class UdonPacker : MonoBehaviour
{
    [SerializeField]MonoBehaviour sourceFile;
    [SerializeField]String sourceFileName;

    [SerializeField]bool useApplicationPath=true;
    [SerializeField]string inputFolderPath;
    [SerializeField]string outputFolderPath;
    string FindFileInDirectory(string directoryPath, string fileName)
{
    var filePaths = Directory.GetFiles(directoryPath, fileName, SearchOption.AllDirectories);
    
    // 最初の一致を返す（存在する場合）
    return filePaths.FirstOrDefault();
}


public SyntaxNode DoPacking(string sourceCode, string sourceType, string directoryPath = "")
{
    return DoPacking(sourceCode, sourceType, new Dictionary<string,string>(), directoryPath);
}
(string InterfaceName, string ClassName)? GetInterfaceAndClassFromAttribute(AttributeSyntax attribute)
{
    if (attribute.ArgumentList?.Arguments.Count == 2)
    {
        var interfaceNameArgument = attribute.ArgumentList.Arguments[0].Expression;
        var classNameArgument = attribute.ArgumentList.Arguments[1].Expression;

        // 文字列引数の場合
        if (interfaceNameArgument is LiteralExpressionSyntax interfaceNameLiteral &&
            interfaceNameLiteral.Token.IsKind(SyntaxKind.StringLiteralToken) &&
            classNameArgument is LiteralExpressionSyntax classNameLiteral &&
            classNameLiteral.Token.IsKind(SyntaxKind.StringLiteralToken))
        {
            return (interfaceNameLiteral.Token.ValueText, classNameLiteral.Token.ValueText);
        }
    }
    return null;
}

public SyntaxNode DoPacking(string sourceCode, string sourceType,Dictionary<string,string> interfaceTypeDict, string directoryPath = "")
{
    if (string.IsNullOrEmpty(directoryPath))
        directoryPath = Application.dataPath;

    var tree = CSharpSyntaxTree.ParseText(sourceCode);
    var root = tree.GetRoot();

    //root = RenameClassIfPresent(root); TODO: fix it
    Debug.Log(root);
    FieldDeclarationSyntax unrollField;
    int loopMax=10;
    while ((unrollField = FindFirstPackingField(root)) != null)
    {
        loopMax--;
        var className = unrollField.Declaration.Type.ToString();
        var newDict=new Dictionary<string,string>(interfaceTypeDict);
        var iToCs=unrollField.AttributeLists
        .SelectMany(attrList => attrList.Attributes)
        .Where(attr => attr.Name.ToString()==nameof(InterfaceToClassAttribute));
        if(iToCs.Count()>1)Debug.LogError("InterfaceToClassAttribute is  duplicated");
        else if(iToCs.Count()==1){
            var info=GetInterfaceAndClassFromAttribute(iToCs.First());
            
            if(info!=null){
                newDict.Add(info.Value.InterfaceName,info.Value.ClassName);
            }
        }
        if(newDict.ContainsKey(className)){
            className=newDict[className];
        }
        

        var correspondingFilePath = FindFileInDirectory(directoryPath, className + ".cs");
        Debug.Log(className);
        if (File.Exists(correspondingFilePath))
        {

            /*var fieldTypeAttributes = unrollField.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Where(attr => attr.Name.ToString().EndsWith("FieldTypeAttribute")) // クラス名でフィルタリング
            .ToList();
*/
            string fileContent = File.ReadAllText(correspondingFilePath);
            var embeddedRoot = DoPacking(fileContent, className, newDict,directoryPath);//CSharpSyntaxTree.ParseText(fileContent).GetRoot();
            embeddedRoot = RemoveOverrideModifier(embeddedRoot);

            root = ProcessMainRootWithEmbeddedRoot(root, unrollField, embeddedRoot, sourceType, className);
        } else {
            Debug.LogWarning("File not found: "+correspondingFilePath);
        }
    }

    return root;
}

SyntaxNode FindFirstWithOverride(SyntaxNode root){
    return root.DescendantNodes()
        .FirstOrDefault(node => 
            (node is MethodDeclarationSyntax method && method.Modifiers.Any(mod => mod.IsKind(SyntaxKind.OverrideKeyword))) ||
            (node is PropertyDeclarationSyntax property && property.Modifiers.Any(mod => mod.IsKind(SyntaxKind.OverrideKeyword)))
        );
}
SyntaxNode RemoveOverrideModifier(SyntaxNode root)
{
    var newRoot=root;
    SyntaxNode node;    
    while((node = FindFirstWithOverride(newRoot))!=null)
    {
        SyntaxTokenList newModifiers;
        if (node is MethodDeclarationSyntax method)
        {
            newModifiers = RemoveOverrideFromModifiers(method.Modifiers);
            var newMethod = method.WithModifiers(newModifiers);
            newRoot = newRoot.ReplaceNode(method, newMethod);
        }
        else if (node is PropertyDeclarationSyntax property)
        {
            newModifiers = RemoveOverrideFromModifiers(property.Modifiers);
            var newProperty = property.WithModifiers(newModifiers);
            newRoot = newRoot.ReplaceNode(property, newProperty);
        } else {
            Debug.LogWarning("not supported member type:"+node.GetType());
        }
    }

    return newRoot;
}
private SyntaxTokenList RemoveOverrideFromModifiers(SyntaxTokenList modifiers)
{
    return SyntaxFactory.TokenList(modifiers.Where(token => !token.IsKind(SyntaxKind.OverrideKeyword)));
}

private SyntaxNode RefactorMethodCallsForPackedObjects(SyntaxNode root, string unrolledVariableName, string className)
{
    var newRoot=root;
    // メンバ呼び出しのパターンを探す
    MemberAccessExpressionSyntax memberCall;
    int limit=10;
    while ((memberCall=FindFirstMemberCall(newRoot,unrolledVariableName)) != null)
    {
        if(IsPossiblyStaticClass(memberCall.Parent))continue;
        // 呼び出しのメンバ名を取得
        var oldMemberName = memberCall.Name.Identifier.Text;
        // 静的メソッドの呼び出しを回避するためのチェック
        /*Debug.Log("parent:" + memberCall.Parent);
        if (memberCall.Parent is InvocationExpressionSyntax invocation)
        {
            var invokedMethod = invocation.Expression as IdentifierNameSyntax;
            if (invokedMethod != null && invokedMethod.Identifier.Text == oldMemberName)
            {
                // 現在のクラスのメソッド
            }else{
                continue;
            }
        }else{
            continue;
        }*/

        // 新しいメンバ名を作成
        var newMemberName = $"{unrolledVariableName}_{oldMemberName}";

        var newReference = SyntaxFactory.IdentifierName(newMemberName);

        // ルート内の古いメンバ呼び出しを新しいものに置き換える
        newRoot = newRoot.ReplaceNode(memberCall, newReference);
    }

    return newRoot;
}

private SyntaxNode RenameClassIfPresent(SyntaxNode root)
{
    var mainClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
    if (mainClass != null)
    {
        var newIdentifier = SyntaxFactory.Identifier("Packed" + mainClass.Identifier.Text);
        var renamedClass = mainClass.WithIdentifier(newIdentifier);
        root = root.ReplaceNode(mainClass, renamedClass);
    }
    return root;
}

private FieldDeclarationSyntax FindFirstPackingField(SyntaxNode root,int skip=0)
{
    var unroll= root.DescendantNodes()
               .OfType<FieldDeclarationSyntax>()
               .Skip(skip)
               .FirstOrDefault(f => f.AttributeLists.Any(attrList => attrList.Attributes.Any(attr => attr.Name.ToString().Contains(nameof(PackingAttribute)))));
    return unroll;
}
private  IEnumerable<FieldDeclarationSyntax> FindInterfaceToTypeInfo(SyntaxNode root)
{
    var unroll= root.DescendantNodes()
               .OfType<FieldDeclarationSyntax>()
               .Where(f => f.AttributeLists.Any(attrList => attrList.Attributes.Any(attr => attr.Name.ToString().Contains(nameof(InterfaceToClassAttribute)))));
    return unroll;
}
private SyntaxNode FindFirstReference(SyntaxNode root,string name,string className)
{
    var oldReferences = root.DescendantNodes()
        .Where(node => node is IdentifierNameSyntax identifier && identifier.Identifier.Text == name)
        .Where(node=>!IsPossiblyStaticClass(node.Parent)  )
        .FirstOrDefault();
    //if(oldReferences!=null&&oldReferences.Parent is MemberAccessExpressionSyntax)Debug.Log("ref parent: "+(oldReferences.Parent as MemberAccessExpressionSyntax ).Expression + "   "+className);
    return oldReferences;
}

private MemberAccessExpressionSyntax FindFirstMemberCall(SyntaxNode root,string name)
{
    var memberCalls = root.DescendantNodes()
                          .OfType<MemberAccessExpressionSyntax>()
                          .Where(m => m.Expression is IdentifierNameSyntax id && id.Identifier.Text == name)
                          .FirstOrDefault();
    //if(oldReferences!=null&&oldReferences.Parent is MemberAccessExpressionSyntax)Debug.Log("ref parent: "+(oldReferences.Parent as MemberAccessExpressionSyntax ).Expression + "   "+className);
    return memberCalls;
}
   
bool IsPossiblyStaticClass(SyntaxNode node)
{
    if(node is MemberAccessExpressionSyntax memberAccess){
        switch (memberAccess.Expression)
        {
            case IdentifierNameSyntax identifier:
                // 識別子の最初の文字が大文字かどうかをチェック
                return char.IsUpper(identifier.Identifier.Text[0]);
            default:
                return false;
        }
    }
    return false;
}
private SyntaxNode ProcessMainRootWithEmbeddedRoot(SyntaxNode root, FieldDeclarationSyntax unrollField, SyntaxNode embeddedRoot, string sourceType, string className)
{
    var newEmbeddedRoot=embeddedRoot;
    var newRoot=root;
    var typeDeclaration = newEmbeddedRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(cls => cls.Identifier.Text == className);

    if (typeDeclaration != null)
    {
        var childHashedMembers = typeDeclaration.Members;
        newEmbeddedRoot = RefactorMemberRefs(newEmbeddedRoot, childHashedMembers, sourceType, className, unrollField.Declaration.Variables.First().Identifier.Text);
        typeDeclaration = newEmbeddedRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(cls => cls.Identifier.Text == className);

        var memberRefs = typeDeclaration.Members.Select(member => RefactorMemberName(member, sourceType, className, unrollField.Declaration.Variables.First().Identifier.Text));
        newRoot = newRoot.InsertNodesAfter(unrollField, memberRefs);
        newRoot=AddUnityMethodIfExists(newRoot,memberRefs, sourceType,unrollField.Declaration.Variables.First().Identifier.Text);

        newRoot = newRoot.RemoveNode(FindFirstPackingField(newRoot), SyntaxRemoveOptions.KeepNoTrivia);
    }

    var usings = newRoot.DescendantNodes().OfType<UsingDirectiveSyntax>().ToList();
    var newUsings = embeddedRoot.DescendantNodes().OfType<UsingDirectiveSyntax>().ToList();
    
    // 新しい using ディレクティブを既存のリストに追加
    usings = usings.Concat(newUsings).ToList();

    // 初めて出てくるUsingDirectiveSyntaxの前に新しいusingを追加
    if (usings.Any())
    {
        newRoot = newRoot.InsertNodesBefore(newRoot.DescendantNodes().OfType<UsingDirectiveSyntax>().First(), newUsings);
    }
    else
    {
        // 既存のusingがない場合、rootの先頭に新しいusingを追加するか、適切な場所を見つけて追加する必要があります。
    }
    var unrolledVariableName = unrollField.Declaration.Variables.First().Identifier.Text;
    newRoot = RefactorMethodCallsForPackedObjects(newRoot, unrolledVariableName, className);

    return newRoot; 
}


string GetMemberName(MemberDeclarationSyntax member)
{
    switch (member)
    {
        case MethodDeclarationSyntax method:
            return method.Identifier.Text;
        case FieldDeclarationSyntax field:
            return field.Declaration.Variables.First().Identifier.Text;
        case PropertyDeclarationSyntax prop:
            return prop.Identifier.Text;
        case EventDeclarationSyntax eventDecl:
            return eventDecl.Identifier.Text;
        case ClassDeclarationSyntax classDecl:
            return classDecl.Identifier.Text;
        case StructDeclarationSyntax structDecl:
            return structDecl.Identifier.Text;
        // ... 他のメンバー型に対するケースを追加 ...
        default:
            return member.ToString() ?? "";
    }
}
private SyntaxNode RefactorMemberRefs(SyntaxNode root,IEnumerable<MemberDeclarationSyntax> childMembers, string sourceType,string memberType, string originalFieldName)
{
    SyntaxNode newRoot=root;
    MemberDeclarationSyntax memberRef;
    //string codeHash = ComputeSha256Hash(sourceCode);
    foreach(var childMember in  childMembers){
        string newMemberName = $"{originalFieldName}_{GetMemberName(childMember)}";
        newRoot=RenameVariableReferences(newRoot,childMember, GetMemberName(childMember), newMemberName,memberType);
        //memberRefs.Add(memberRef);
    }
    // ... and so on for other member types

    return newRoot;
}

private MemberDeclarationSyntax RefactorMemberName(MemberDeclarationSyntax member, string sourceType, string memberType, string originalFieldName)
{
    Debug.Log(GetMemberName(member));
    string newMemberName = $"{originalFieldName}_{GetMemberName(member)}";

    if (member is FieldDeclarationSyntax field)
    {
        var oldVariable = field.Declaration.Variables.First();
        var newVariable = RenameVariable(oldVariable, newMemberName);
        var newDeclaration = field.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(newVariable));
        return field.WithDeclaration(newDeclaration);
    }
    else if (member is MethodDeclarationSyntax method)
    {
        return method.WithIdentifier(SyntaxFactory.Identifier(newMemberName));
    } else if(member is PropertyDeclarationSyntax property){
        return property.WithIdentifier(SyntaxFactory.Identifier(newMemberName));
    } else{
        Debug.LogWarning("not supported member type:"+member.GetType());
    }

    return member;
}
private VariableDeclaratorSyntax RenameVariable(VariableDeclaratorSyntax variable, string newName)
{
    return variable.WithIdentifier(SyntaxFactory.Identifier(newName));
}

string[] unityLifecycleMethods = new[]
    {
        "Start",
        "Update",
        "FixedUpdate",
        "LateUpdate",
        "OnGUI",
        "OnDisable",
        "OnEnable",
        "OnDeserialization",
        "OnPreSerialization"
        // ... その他のライフサイクルメソッドを追加
    };
public SyntaxNode AddUnityMethodIfExists(SyntaxNode root,IEnumerable<MemberDeclarationSyntax> members,string sourceType,  string originalFieldName)
{
    var newRoot=root;
    foreach(var member in members){
        foreach (var methodName in unityLifecycleMethods)
        {
           
            var hashedName=originalFieldName+"_"+methodName;
            if(GetMemberName(member)==hashedName){
                 var targetMethod = newRoot.DescendantNodes()
                                .OfType<MethodDeclarationSyntax>()
                                .FirstOrDefault(m => m.Identifier.Text == methodName);
                if (targetMethod==null){
                    var newMethod= SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), methodName)
                                                .WithBody(SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(SyntaxFactory.ParseExpression($"{originalFieldName}_{methodName}()"))));
                    var classDeclaration = newRoot.DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        .FirstOrDefault(c => c.Identifier.Text == sourceType);
                    Debug.Log(classDeclaration);
                    Debug.Log(newMethod);
                    if (classDeclaration != null)
                    {
                        newRoot = newRoot.InsertNodesAfter(classDeclaration.Members.Last(), new[] { newMethod });
                    }
                    targetMethod = newRoot.DescendantNodes()
                                    .OfType<MethodDeclarationSyntax>()
                                    .FirstOrDefault(m => m.Identifier.Text == methodName);
                }
                else {                    
                    // そのメソッド名が存在する場合の処理
                    
                        // 新しい'hashed_MethodName()'メソッドを作成
                        var hashedMethodInvocation = SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.ParseExpression($"{hashedName}()"));
                        Debug.Log(hashedMethodInvocation);
                        // 新しいメソッドをライフサイクルメソッドの中で呼び出すように修正
                        var newStatements = targetMethod.Body.Statements.Insert(0, hashedMethodInvocation);
                        var newTargetMethod = targetMethod.WithBody(targetMethod.Body.WithStatements(newStatements));

                        // 古いメソッドを新しいもので置き換え
                        newRoot = newRoot.ReplaceNode(targetMethod, newTargetMethod);
                        
                }
                break;
            }
           
        }
    }

    return newRoot;
}
private SyntaxNode RenameVariableReferences(SyntaxNode root,MemberDeclarationSyntax member, string oldName, string newName,string className)
{
    // 旧い変数名の参照を見つける
    Debug.Log(GetMemberName(member));

    SyntaxNode newRoot = root;
    SyntaxNode oldRef=null;
    // 各参照を新しい変数名で置き換える
    int limit=10;
    while ((oldRef  =FindFirstReference(newRoot,oldName,className))!=null)
    {
        limit--;
        if (oldRef is IdentifierNameSyntax)
        {
            var newIdentifier = SyntaxFactory.IdentifierName(newName);
            newRoot = newRoot.ReplaceNode(oldRef, newIdentifier);
        }
        else if (oldRef is MemberAccessExpressionSyntax memberAccess)
        {
            var newMemberAccess = memberAccess.WithName(SyntaxFactory.IdentifierName(newName));
            newRoot = newRoot.ReplaceNode(oldRef, newMemberAccess);
        } else{
            Debug.LogWarning("not supported member type:"+member.GetType());
        }
    }


    //var newMamberRef=RenameMemberReferences(member,oldName,newName);
    return newRoot;
}
private MemberDeclarationSyntax RenameMemberReferences(MemberDeclarationSyntax member, string oldName, string newName)
{
    var oldIdentifier = SyntaxFactory.IdentifierName(oldName);
    var newIdentifier = SyntaxFactory.IdentifierName(newName);

    var newMember = member.ReplaceNode(oldIdentifier,newIdentifier);/* .ReplaceNodes(
        member.DescendantNodes().OfType<IdentifierNameSyntax>(), 
        (node, _) => node.IsEquivalentTo(oldIdentifier) ? newIdentifier : node
    );*/

    return newMember;
}
bool all=false;
    void Start()
    {
        //SavePackedClassToFile();
    }
[SerializeField]String outputFileExtension="cs";
public void SavePackedClassToFile()
{
    //Debug.Assert(target != null, "target != null");
    Debug.Log("Application.dataPath = "+Application.dataPath);
    string type=sourceFile?sourceFile.GetType().ToString() : sourceFileName;
    Debug.Assert(!string.IsNullOrEmpty(type), "source file name is empty");
    string fixedInputFolderPath=FixPath(inputFolderPath);
    string fixedOutputFolderPath=FixPath(outputFolderPath);
    string inputDir = useApplicationPath? Path.Combine(Application.dataPath, fixedInputFolderPath):fixedInputFolderPath;
    string outputDir = useApplicationPath?  Path.Combine(Application.dataPath, fixedOutputFolderPath):fixedOutputFolderPath;
    var filePath=FindFileInDirectory(inputDir,type+".cs");
    Debug.Log("filePath = "+filePath);
    Debug.Assert(!string.IsNullOrEmpty(filePath), "source file not found");
    string sourceCode = File.ReadAllText(filePath);
    SyntaxNode node;
    node=DoPacking(sourceCode,type, inputDir);
    node=RenameClassIfPresent(node);
    var outputFileName="Packed"+type+"."+outputFileExtension;
    // ファイルを保存
    File.WriteAllText(Path.Combine(outputDir, outputFileName), node.NormalizeWhitespace().ToFullString());
}

string FixPath(string path){
    path = path.Replace(@"\", "/");
    return path;
}

}
}

#endif