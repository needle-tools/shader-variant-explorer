using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Experimental;
using UnityEditorInternal;
using UnityEngine;
using Debug = UnityEngine.Debug;

// TODO should use UnityDataTools.FileSystem instead of reinventing the wheel here
// see https://github.com/Unity-Technologies/UnityDataTools/blob/main/TextDumper/TextDumperTool.cs

public class ShaderVariantParser : MonoBehaviour
{
    [MenuItem("internal:Window/Needle/Shader Variant Explorer - Get Artifacts")]
    private static void GetPathsForSelected()
    {
        var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);

        StringBuilder assetPathInfo = new StringBuilder();

        var guidString = AssetDatabase.AssetPathToGUID(assetPath);
        //The ArtifactKey is needed here as there are plans to
        //allow importing for different platforms without switching
        //platform, thus ArtifactKeys will be parametrized in the future
        var artifactKey = new ArtifactKey(new GUID(guidString));
        var artifactID = AssetDatabaseExperimental.LookupArtifact(artifactKey);

        //Its possible for an Asset to have multiple import results,
        //if, for example, Sub-assets are present, so we need to iterate
        //over all the artifacts paths
        AssetDatabaseExperimental.GetArtifactPaths(artifactID, out var paths);

        assetPathInfo.Append($"Files associated with {assetPath}");
        assetPathInfo.AppendLine();

        foreach (var curVirtualPath in paths)
        {
            //The virtual path redirects somewhere, so we get the
            //actual path on disk (or on the in memory database, accordingly)
            var curPath = Path.GetFullPath(curVirtualPath);
            assetPathInfo.Append("\t" + curPath);
            assetPathInfo.AppendLine();
        }

        Debug.Log("Path info for asset:\n" + assetPathInfo.ToString());

        var firstPath = paths.FirstOrDefault();
        if (firstPath != null) {
            EditorUtility.RevealInFinder(firstPath);
            ParseShaderArtifact(firstPath);
        }

        // InternalEditorUtility.OpenFileAtLineExternal(paths.First(), 0);
    }

    private static void ParseShaderArtifact(string artifactPath)
    {
        // we run the artifact through binary2text.exe shipped with the Unity installation
        var path = Path.GetDirectoryName(EditorApplication.applicationPath) + "/Data/Tools/binary2text.exe";

        if (!File.Exists(path))
        {
            Debug.LogError("Please report a bug - looks like we can't find binary2text.exe which should ship with your Editor installation.");
        }

        var proc = new ProcessStartInfo();
        proc.UseShellExecute = false;
        proc.CreateNoWindow = true;
        proc.FileName = path;
        var tempFile = Path.GetTempFileName();
        proc.Arguments = "\"" + Path.GetFullPath(artifactPath) + "\"" + " " + tempFile;
        
        var pi = Process.Start(proc);
        pi.EnableRaisingEvents = true;
        
        Debug.Log("Starting process " + proc.FileName + " " + proc.Arguments);
        
        pi.Exited += (sender, args) =>
        {
            Debug.Log("Complete");
            try
            {
	            ParseTextForm(File.ReadAllLines(tempFile));
            }
            catch (Exception e)
            {
	            Debug.LogException(e);
            }
        };

        // var bytes = File.ReadAllBytes(artifactPath);
        // var lineCount = 0;
        // for (int i = 0; i < bytes.Length; i++)
        // {
        //     if (bytes[i] == '\n')
        //     {
        //         lineCount++;
        //     }
        // }
        // Debug.Log(lineCount);
        //
        // var allText = File.ReadAllText(artifactPath);
        // var tmpFile = Application.dataPath + "/temp.txt";
        // File.WriteAllText(tmpFile, allText);
        // InternalEditorUtility.OpenFileAtLineExternal(tmpFile, 0);
        //
        // var lines0 = allText.Substring(allText.LastIndexOf('}'));
        // Debug.Log(lines0);
        // var lines = lines0.Split(new char[] { (char) 0}, StringSplitOptions.RemoveEmptyEntries);
        //
        // // Debug.Log(File.ReadAllText(artifactPath));
        // // Debug.Log("Lines: " + File.ReadAllLines(artifactPath).Length);
        // // var lines = File.ReadAllLines(artifactPath).Reverse().Take(10);
        // Debug.Log($"End of file [{lines.Count()}]:\n" + string.Join(",", lines));
    }

    /*
    Snippet from binary2text.exe on 2020.3.33f1
    
    m_ParsedForm
        m_PropInfo
        m_SubShaders
            size 1 (int)
            data (SerializedSubShader)
                m_Passes (vector)
                    size 4 (int)
                    data  (SerializedPass)
                        m_State  (SerializedShaderState)
                            m_Name "FORWARD" (string)
                            zTest  (SerializedShaderFloatValue)
								val 4 (float)
								name "<noninit>" (string)
							zWrite  (SerializedShaderFloatValue)
								val 1 (float)
								name "<noninit>" (string)
                            culling  (SerializedShaderFloatValue)
							    val 0 (float)
							    name "_Cull" (string)
							conservative  (SerializedShaderFloatValue)
								val 0 (float)
								name "<noninit>" (string)
							offsetFactor  (SerializedShaderFloatValue)
								val 0 (float)
								name "<noninit>" (string)
							offsetUnits  (SerializedShaderFloatValue)
								val 0 (float)
								name "<noninit>" (string)
							alphaToMask  (SerializedShaderFloatValue)
								val 0 (float)
								name "<noninit>" (string)
							stencilOp  (SerializedStencilOp)
							pass  (SerializedShaderFloatValue)
								val 0 (float)
								name "<noninit>" (string)
							fail  (SerializedShaderFloatValue)
								val 0 (float)
								name "<noninit>" (string)
							zFail  (SerializedShaderFloatValue)
								val 0 (float)
								name "<noninit>" (string)
							comp  (SerializedShaderFloatValue)
								val 8 (float)
								name "<noninit>" (string)
							m_Tags  (SerializedTagMap)
								tags  (map)
									size 3 (int)
									data  (pair)
										first "LIGHTMODE" (string)
										second "FORWARDBASE" (string)
									data  (pair)
										first "RenderType" (string)
										second "Opaque" (string)
									data  (pair)
										first "SHADOWSUPPORT" (string)
										second "true" (string)
							lighting 0 (bool)
                m_Tags (SerializedTagMap)
                    tags (map)
                        size 1 (int)
                        data (pair)
                            first "RenderType" (string)
                            second "Opaque" (string)
    */

    [Conditional("EXTENDED_SHADER_VARIANT_LOGS")]
    static void Log(object o, UnityEngine.Object o2 = null)
    {
	    Debug.Log(o, o2);
    }
    
    private static void ParseTextForm(string[] lines)
    {
        // find lines that contains a shader
        var count = lines.Length;
        int line = 0;
        while (line < count)
        {
            // find shaders
            while (line < count - 1 && !lines[line].Contains("(ClassID: 48)")) 
                line++;
            
            Log("Found shader at " + line);
            
            // find "m_parsedForm"
            while (line < count - 1 && !lines[line].Contains("m_ParsedForm"))
                line++;

            var startLine = line;
            Log("Found parsed form at " + line);
            
            // count indentation
            var currentIndentation = lines[line].IndexOf("m_ParsedForm", StringComparison.Ordinal) + 1;
            var startString = "".PadRight(currentIndentation, '\t');

            while (line < count - 1 && !lines[line].Contains("m_SubShaders"))
	            line++;
            line++; // array size
            
            if (line >= count)
	            break;
            
            // no need to parse m_PropInfo.m_Props - seems to be all accessible via API already
            // parse m_SubShaders array - that contains pipeline state info
            Log("length line " + lines[line]);
            var subShaderCount = int.Parse(GetBetween(lines[line], "size", "(int)"));
            Debug.Log("<b>Subshaders</b>: " + subShaderCount);
            
	        // TODO parse pass array
	        // TODO get name and tags for each pass
	        // TODO get local and global keyword mask for each pass

            static string GetBetween(string full, string prefix, string postfix)
            {
	            var start = full.LastIndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
	            var end = full.IndexOf(postfix, StringComparison.Ordinal);
	            
	            if (end < 0) return full.Substring(start).Trim();
	            return full.Substring(start, end - start).Trim();
            }

            var stateDict = new Dictionary<string, (string valuePropertyName, float value)>();
            
            void LogLastPass()
            {
	            Debug.Log("<b>Pass State</b>\n" + string.Join("\n", stateDict.Select(x => 
		            x.Key + " → " + x.Value.value + (x.Value.valuePropertyName != null ? " [" + x.Value.valuePropertyName + "]": ""))));
            }

            int passCount = 0;
            // find end line where next indentation is less or equal than start
            while (line < count && string.IsNullOrWhiteSpace(lines[line]) || lines[line].StartsWith(startString, StringComparison.Ordinal))
            {
	            const string passMarker = "data  (SerializedPass)";
	            string currentIndent = "";
	            if (lines[line].Contains(passMarker))
	            {
		            var currentIndentIndex = lines[line].IndexOf(passMarker, StringComparison.Ordinal);
		            currentIndent = lines[line].Substring(0, currentIndentIndex) + "\t"; // one more
		            
		            // start new shader pass
		            if(passCount > 0) 
			            LogLastPass();
		            
		            stateDict.Clear();
		            
		            passCount++;
	            }
	            
	            // TODO get right pass name
	            // TODO Maybe we can get away with the pass index?
	            // if(lines[line].Contains("m_Name")) {
		           //  Debug.Log(currentIndent.Length + "|" + lines[line]);
		           //  Debug.Log("Pass Name: " + GetBetween(lines[line], "m_Name", "(string)"));
	            // }
		            
	            if (lines[line].EndsWith("(SerializedShaderFloatValue)", StringComparison.Ordinal))
	            {
		            var propName = GetBetween(lines[line], "\t", "(SerializedShaderFloatValue)");
		            
		            // next two lines contain 'val X (float)' and 'name "somename"|"<noninit>" (string)'
		            var valueLine = lines[line + 1];
		            var nameLine = lines[line + 2];

		            var valueString = GetBetween(valueLine, "val", "(float)");
		            var value = float.Parse(valueString);
		            var nameString = GetBetween(nameLine, "name", "(string)").Trim('\"');
		            if (nameString == "<noninit>") nameString = null; // no property name value

		            stateDict[propName] = (nameString, value);
		            line += 2;
	            }
	            else if (lines[line].EndsWith("(SerializedStencilOp)", StringComparison.Ordinal))
	            {
		            
	            }
	            line++;
            }

            LogLastPass();
            
            var endLine = line;

            var shaderData = lines.Skip(startLine).Take(endLine - startLine).ToArray();
            var tmpFile = Path.GetTempFileName();
            File.WriteAllLines(tmpFile, shaderData);
            EditorApplication.delayCall += () =>
            {
	            InternalEditorUtility.OpenFileAtLineExternal(tmpFile, 0, 0);
            };
            // Debug.Log("Found shader data:\n" + string.Join("\n", shaderData));

            // parse shader data into convenient dictionary
        }

        // void Parse(string[] lines, ref int index)
        // {
	       //  if (lines[index].EndsWith("(vector)", StringComparison.Ordinal))
	       //  {
		      //   index++;
		      //   var arraySize = ParseArraySize(lines, index);
		      //   for (int = 0; i < arraySize; i++)
		      //   {
			     //    
		      //   }
	       //  }
	       //  else if ()
        // }
    }
}
