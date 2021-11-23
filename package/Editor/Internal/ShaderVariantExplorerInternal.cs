using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class ShaderVariantExplorerInternal
{
    public static string[] GetShaderGlobalKeywords(Shader s) => ShaderUtil.GetShaderGlobalKeywords(s).OrderBy(x => x).ToArray();
    public static string[] GetShaderLocalKeywords(Shader s) => ShaderUtil.GetShaderLocalKeywords(s).OrderBy(x => x).ToArray();

    public static ShaderData.PreprocessedVariant PreprocessShaderVariant(
        Shader shader,
        int subShaderIndex,
        int passId,
        ShaderType shaderType,
        BuiltinShaderDefine[] platformKeywords,
        string[] keywords,
        ShaderCompilerPlatform shaderCompilerPlatform,
        BuildTarget buildTarget,
        GraphicsTier tier,
        bool stripLineDirectives)
    {
        // platformKeywords = ShaderUtil.GetShaderPlatformKeywordsForBuildTarget()
        return ShaderUtil.PreprocessShaderVariant(shader, subShaderIndex, passId, shaderType, platformKeywords, keywords, shaderCompilerPlatform, buildTarget, tier, stripLineDirectives);
    }
}
