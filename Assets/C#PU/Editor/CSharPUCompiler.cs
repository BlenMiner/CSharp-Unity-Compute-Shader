using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Debug = UnityEngine.Debug;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using System.IO;

namespace CSharpPU.Editor
{
    public class CSharPUCompiler : UnityEditor.AssetModificationProcessor
    {
        public static IEnumerable<Type> FindDerivedTypes(Assembly assembly, Type baseType)
        {
            return assembly.GetTypes().Where(t => baseType.IsAssignableFrom(t) && t != baseType);
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        public static void OnScriptsReloaded() 
        {
            EditorUtility.DisplayProgressBar("Compiling C#UP Shaders", "Bootstrapping ...", 0f);
            
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            List<Type> shaderTypes = new List<Type>(20);

            HashSet<string> shaderNames = new HashSet<string>();

            for (int i = 0; i < assemblies.Length; ++i)
            {
                var asm = assemblies[i];

                EditorUtility.DisplayProgressBar("Compiling C#UP Shaders", "Scanning for types ...", (i + 1) / (float)assemblies.Length);

                foreach(var shader in FindDerivedTypes(asm, typeof(ComputeShaderBase)))
                {
                    shaderTypes.Add(shader);
                    shaderNames.Add(shader.Name);
                }
            }

            var generatedShaders = AssetDatabase.FindAssets("C#PU- t:computeshader");

            for (int i = 0; i < generatedShaders.Length; ++i)
            {
                string path = AssetDatabase.GUIDToAssetPath(generatedShaders[i]);
                FileInfo file = new FileInfo(path);

                string name = file.Name.Substring(5, file.Name.Length - 8);

                if (!shaderNames.Contains(name))
                    AssetDatabase.DeleteAsset(path);
            }

            for (int i = 0; i < shaderTypes.Count; ++i)
            {
                var shader = shaderTypes[i];

                EditorUtility.DisplayProgressBar("Compiling C#UP Shaders", $"Compiling {shader.Name} ...", (i + 1) / (float)shaderTypes.Count);

                ImportShader(shader);
            }

            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
        }

        private static void ImportShader(Type shader)
        {
            ComputeShaderBase.s_LastPath = null;
            ComputeShaderBase instance = (ComputeShaderBase)Activator.CreateInstance(shader, new object[] {null});

            if (ComputeShaderBase.s_LastPath == null)
            {
                Debug.LogError($"C#PU: Shader {shader.Name} doesn't call base class. Consider adding this: <b>public {shader.Name}(ComputeShader shader = null) : base(shader) {{}}</b>");
                return;
            }

            string filePath = ComputeShaderBase.s_LastPath;
            string sourceCode = File.ReadAllText(filePath);
            
            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetCompilationUnitRoot();

            var node = (from classDeclaration in root.DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        where classDeclaration.Identifier.ValueText == shader.Name
                        select classDeclaration).First();

            if (node == null)
                    Debug.LogError($"C#PU: Failed to parse shader, \"{shader.Name}\"");
            else CompileShader(filePath, node);
        }

        private static void CompileShader(string csFilePath, ClassDeclarationSyntax shaderDeclaration)
        {
            CSharPUTranspiler transpiler = new CSharPUTranspiler(csFilePath, shaderDeclaration);
            
            string transpiledCode = transpiler.GetShaderCode();

            FileInfo file = new FileInfo(csFilePath);

            string resultingPath = file.Directory.FullName + "/C#PU-" + shaderDeclaration.Identifier + ".compute";
            File.WriteAllText(resultingPath, transpiledCode);
        }
    }
}