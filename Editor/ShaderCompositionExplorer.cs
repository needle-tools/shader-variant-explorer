using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Needle.Rendering.Editor
{
    public class ShaderCompositionExplorer : EditorWindow
    {
        public Shader shader;
        
        [MenuItem("Needle/Shader Composition Explorer")]
        static void ShowNow() => GetWindow<ShaderCompositionExplorer>().Show();

        [Serializable]
        public class MessageData
        {
            public string fullMessage;
            public string messageWithoutDetails;
            public string messageDetails;
            public string sortedKeywords;
        }
        
        [Serializable]
        public class ListViewData : ScriptableObject
        {
            public List<MessageData> messages = new List<MessageData>();
            public List<LineSection> sections = new List<LineSection>();
        }

        public ListViewData listViewData;
        public SerializedObject tempDataSerializedObject;

        public List<Variant> availableVariants;
        public bool collapseLines;
        private ListView errorScrollView;

        class KeywordBreadcrumbs : ToolbarBreadcrumbs
        {
            // ReSharper disable once InconsistentNaming
            public event Action onSelectionChanged;
            
            private List<string> availableKeywords = new List<string>();
            private List<string> selectedKeywords = new List<string>();
            
            void AddKeyword(object keyword)
            {
                if (keyword is string s)
                {
                    selectedKeywords.Add(s);
                    onSelectionChanged?.Invoke();
                    BuildBreadcrumbs();
                }
            }

            void RemoveKeyword(string keyword)
            {
                if (selectedKeywords.Contains(keyword))
                {
                    selectedKeywords.Remove(keyword);
                    onSelectionChanged?.Invoke();
                    BuildBreadcrumbs();
                }
            }
            
            void AddKeywordMenu()
            {
                var menu = new GenericMenu();
                foreach(var c in availableKeywords) {
                    if(!selectedKeywords.Contains(c))
                        menu.AddItem(new GUIContent(c), false, AddKeyword, c);
                }
                menu.ShowAsContext();
            }
        
            void BuildBreadcrumbs()
            {
                Clear();
                foreach(var k in selectedKeywords)
                    PushItem(k, () => RemoveKeyword(k));
                PushItem("+", AddKeywordMenu);
            }

            public void SetSelectedKeywords(List<string> selected, bool notify)
            {
                selectedKeywords.Clear();
                if(selected != null)
                    selectedKeywords.AddRange(selected.Where(availableKeywords.Contains).OrderBy(x => x.TrimStart('_')));
                if(notify) onSelectionChanged?.Invoke();
                BuildBreadcrumbs();
            }
            
            public void SetAvailableKeywords(List<string> available)
            {
                availableKeywords = available.ToList();
                selectedKeywords = selectedKeywords.Where(availableKeywords.Contains).ToList();
                SetEnabled(availableKeywords.Any());
                BuildBreadcrumbs();
            }

            public void SetSelectedKeywords(string unsortedKeywords, bool notify)
            {
                var keywords = unsortedKeywords?.Split(' ').OrderBy(x => x.TrimStart('_')).ToList();
                SetSelectedKeywords(keywords, notify);
            }

            public string GetSortedKeywordString()
            {
                if (!selectedKeywords.Any()) return null;
                return string.Join(" ", selectedKeywords.OrderBy(x => x.TrimStart('_')));
            }
        }
            
        KeywordBreadcrumbs globalBreadcrumbs, localBreadcrumbs;

        private void OnEnable()
        {
            titleContent = new GUIContent("Shader Composition");
            listViewData = CreateInstance<ListViewData>();
            
            var root = new VisualElement();
            rootVisualElement.Add(root);

            var toolbar = new Toolbar();
            var shaderField = new ObjectField()
            {
                objectType = typeof(Shader)
            };
            shaderField.RegisterValueChangedCallback(x =>
            {
                if (x.newValue is Shader newShader)
                    SetViewedShader(newShader);
            });
            if(shader)
                shaderField.value = shader;
            
            toolbar.Add(new ToolbarButton(() =>
            {
                SetViewedShader(shader);
            }) { text = "Reload "});
            
            toolbar.Add(shaderField);
            
            // var search = new ToolbarPopupSearchField();
            // toolbar.Add(search);

            void SelectVariant(object userData)
            {
                if (userData is Variant variant)
                {
                    globalBreadcrumbs.SetSelectedKeywords(variant.globalKeywords, false);
                    localBreadcrumbs.SetSelectedKeywords(variant.localKeywords, false);
                    KeywordSelectionChanged();
                }
            }
            
            var allCombinationSelector = new ToolbarButton(() =>
            {
                var menu = new GenericMenu();
                foreach (var variant in availableVariants)
                {
                    var hasLocalKeywords = !string.IsNullOrEmpty(variant.localKeywords);
                    menu.AddItem(new GUIContent(variant.globalKeywords + " _" + (hasLocalKeywords ? ("/" + variant.localKeywords + " _") : "")), false, SelectVariant, variant);
                }
                menu.ShowAsContext();
            })
            {
                text = "Select Keyword Combination",
            };
            toolbar.Add(allCombinationSelector);

            var toggleFileCollapse = new ToolbarToggle()
            {
                text = "Collapse Files",
                value = collapseLines
            };
            toggleFileCollapse.RegisterValueChangedCallback(evt =>
            {
                collapseLines = evt.newValue;
                KeywordSelectionChanged();
            });
            toolbar.Add(toggleFileCollapse);
            root.Add(toolbar);

            var globalKeywordToolbar = new Toolbar();
            globalKeywordToolbar.Add(new Label("Global Keywords") { style = {width = 100}});
            globalBreadcrumbs = new KeywordBreadcrumbs();
            globalBreadcrumbs.onSelectionChanged += KeywordSelectionChanged;
            globalKeywordToolbar.Add(globalBreadcrumbs);
            
            root.Add(globalKeywordToolbar);

            var localKeywordToolbar = new Toolbar();
            localKeywordToolbar.Add(new Label("Local Keywords ") { style = {width = 100}});
            localBreadcrumbs = new KeywordBreadcrumbs();
            localBreadcrumbs.onSelectionChanged += KeywordSelectionChanged;
            localKeywordToolbar.Add(localBreadcrumbs);
            
            root.Add(localKeywordToolbar);

            var split = new TwoPaneSplitView(0, 60, TwoPaneSplitViewOrientation.Vertical)
            {
                style = {height = 10000}
            };
            root.Add(split);
            
            errorScrollView = new ListView() {
                itemHeight = 60,
                makeItem = () =>
                {
                    Debug.Log("Making Item");
                    var v = new VisualElement() {
                        style = {flexDirection = FlexDirection.Column}
                    };
                    v.Add(new Label("(none)") {
                        name = "Message",
                        style = {overflow = Overflow.Hidden}
                    });
                    v.Add(new Label("") {
                        name = "Keywords",
                        style = {overflow = Overflow.Hidden}
                    });
                    return v;
                },
                bindItem = (element, i) =>
                {
                    var error = (listViewData && i < listViewData.messages.Count && i >= 0) ? listViewData.messages[i] : null;
                    element.Q<Label>("Message").text = error?.messageWithoutDetails ?? "(no message)";
                    element.Q<Label>("Keywords").text = error?.sortedKeywords ?? "(no keywords)";
                },
                bindingPath = nameof(ListViewData.messages),
                style = {
                    display = DisplayStyle.Flex,
                    flexGrow = 1,
                    minHeight = 20,
                    unityOverflowClipBox = OverflowClipBox.ContentBox,
                    overflow = Overflow.Hidden
                },
                showBoundCollectionSize = false,
            };
            errorScrollView.onItemsChosen += objects =>
            {
                var msg = listViewData.messages[errorScrollView.selectedIndex];
                globalBreadcrumbs.SetSelectedKeywords(msg.sortedKeywords, true);
            };
            
            tempDataSerializedObject = new SerializedObject(listViewData);
            errorScrollView.Bind(tempDataSerializedObject);
            split.Add(errorScrollView);

            var codeScrollView = new ListView()
            {
                itemHeight = 20,
                makeItem = () =>
                {
                    var v = new VisualElement() {style = {flexDirection = FlexDirection.Row}};
                    v.Add(new Label("000000") {name = "LineNumber", style =
                    {
                        overflow = Overflow.Hidden, 
                        fontSize = 9, 
                        color = new Color(1,1,1,0.5f),
                        marginRight = 10,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        width = 40,
                    }});
                    v.Add(new Label("000000") {name = "LineIndex", style =
                    {
                        overflow = Overflow.Hidden, 
                        fontSize = 9, 
                        color = new Color(1,1,1,0.5f),
                        marginRight = 10,
                        unityTextAlign = TextAnchor.MiddleRight,
                        width = 40,
                    }});
                    v.Add(new Label("Line XYZ") {name = "LineContent", style =
                    {
                        overflow = Overflow.Hidden,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        flexGrow = 1,
                    }});
                    v.Add(new Label("some.shader") {name = "File", style =
                    {
                        overflow = Overflow.Hidden, 
                        color = new Color(1,1,1,0.5f),
                        unityTextAlign = TextAnchor.LowerRight,
                    }});
                    return v;
                },
                bindItem = (element, i) =>
                {
                    var error = (listViewData && i < listViewData.sections.Count && i >= 0) ? listViewData.sections[i] : null;
                    element.Q<Label>("LineNumber").text = i.ToString("000000");
                    element.Q<Label>("LineIndex").text = error?.lineIndex.ToString("000000") ?? "------";
                    element.Q<Label>("LineContent").text = error?.lineContent ?? "(empty)";
                    element.Q<Label>("File").text = error?.fileNameDisplay ?? "";
                },
                bindingPath = nameof(listViewData.sections),
                showBoundCollectionSize = false,
            };
            codeScrollView.Bind(tempDataSerializedObject);
            codeScrollView.onItemsChosen += objects =>
            {
                var selectedSection = listViewData.sections[codeScrollView.selectedIndex];
                // find file
                var file = "";
                for (int index = codeScrollView.selectedIndex; index >= 0; index--)
                {
                    if (listViewData.sections[index].fileSectionStart != null)
                    {
                        file = listViewData.sections[index].fileSectionStart;
                        break;
                    }
                }
                Debug.Log("File: " + file + ", line: " + selectedSection.lineIndex);
                
                if(File.Exists(file))
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(file, selectedSection.lineIndex);
            };
            split.Add(codeScrollView);
        }

        private void KeywordSelectionChanged()
        {
            var sections = availableVariants.FirstOrDefault(x =>
                    x.globalKeywords == globalBreadcrumbs.GetSortedKeywordString() &&
                    x.localKeywords == localBreadcrumbs.GetSortedKeywordString()
                )?
                .mapping
                .SelectMany(x => x.lines)
                .Where(x => !collapseLines || x.fileSectionStart != null);
            listViewData.sections = sections?.ToList();
            tempDataSerializedObject.Update();
            
            Debug.Log("Total number of lines in variant: " + listViewData.sections?.Count);
        }

        [NonSerialized] private string editorRoot = null;
        [NonSerialized] private string cgIncludesRoot = null;
        [NonSerialized] private string packageCacheRoot = null;
        
        string StripProjectRelativePath(string absolutePath)
        {
            if (editorRoot == null) editorRoot = Path.GetDirectoryName(EditorApplication.applicationPath)?.Replace("\\", "/") + "/";
            if (cgIncludesRoot == null) cgIncludesRoot = Path.GetDirectoryName(EditorApplication.applicationPath)?.Replace("\\", "/") + "/Data/CGIncludes/";
            if (packageCacheRoot == null) packageCacheRoot = Path.GetDirectoryName(Application.dataPath)?.Replace("\\","/") + "/Library/PackageCache/";
            
            if (absolutePath.StartsWith(cgIncludesRoot, StringComparison.OrdinalIgnoreCase)) return absolutePath.Substring(cgIncludesRoot.Length);
            if (absolutePath.StartsWith(editorRoot, StringComparison.OrdinalIgnoreCase)) return absolutePath.Substring(editorRoot.Length);
            if (absolutePath.StartsWith(packageCacheRoot, StringComparison.OrdinalIgnoreCase))
            {
                var subPath = absolutePath.Substring(packageCacheRoot.Length);
                var slashIndex = subPath.IndexOf('/');
                var atIndex = subPath.IndexOf('@');
                
                if (slashIndex <= -1 || atIndex <= -1) return subPath;
                
                var lastPart = subPath.Substring(slashIndex);
                var packagePart = subPath.Substring(0, atIndex);
                return "Packages/" + packagePart + lastPart;
            }
            
            // TODO could be a local package, we could still rewrite as Packages/ path
            return absolutePath;
        }
        
        void SetViewedShader(Shader selectedShader)
        {
            shader = selectedShader;

            // fetch all compilation error messages
            int shaderMessageCount = ShaderUtil.GetShaderMessageCount(shader);
            var shaderMessages = (ShaderMessage[]) null;
            if (shaderMessageCount >= 1)
                shaderMessages = ShaderUtil.GetShaderMessages(shader);

            if (shaderMessages != null)
            {
                var allErrors = shaderMessages
                    .Where(x => x.severity == ShaderCompilerMessageSeverity.Error);
                
                listViewData.messages.Clear();
                listViewData.messages.AddRange(allErrors.Select(x => new MessageData()
                {
                   fullMessage = ToMessageString(x),
                   messageWithoutDetails = ToMessageStringWithoutDetails(x),
                   sortedKeywords = SortedKeywords(x),
                }));
                // Debug.Log("Number of messages: " + tempData.messages.Count);
                tempDataSerializedObject.Update();
            }
            else
            {
                // Debug.Log("No Shader Messages for " + shader, shader);
            }

            // fetch local and global keywords for this shader
            // get shader info
            GetShaderDetails(shader, out var variantCount, out string[] localKeywords, out string[] globalKeywords);
            var globalKeywordsList = globalKeywords.ToList();
            // not sure why this has to be added (doesn't show in the keyword list returned by Unity); potentially others have to be added as well?
            globalKeywordsList.Add("STEREO_INSTANCING_ON");
            globalBreadcrumbs.SetAvailableKeywords(globalKeywordsList);
            localBreadcrumbs.SetAvailableKeywords(localKeywords.ToList());
            
            // fetch the entire preprocessed file
            CompileShader(shader, false);
            
            // check if file exists:
            var expectedFilePath = "Temp/Preprocessed-" + shader.name.Replace('/', '-').Replace('\\', '-') + ".shader";
            // Debug.Log("Checking for file: " + expectedFilePath + ", exists: " + File.Exists(expectedFilePath));
            if(File.Exists(expectedFilePath))
            {
                // read entire file into memory, and parse it one by one - might change between Unity versions
                var lines = File.ReadAllLines(expectedFilePath);

                var variants = new List<Variant>();
                var currentVariant = default(Variant);
                var currentFileSection = default(FileSection);
                var currentLineIndex = 0;

                const string SeparatorLine = @"//////////////////////////////////////////////////////";
                const string GlobalKeywordsStart = @"Global Keywords: ";
                const string LocalkeywordsStart = @"Local Keywords: ";
                const string LineStart = @"#line ";

                var sb = new StringBuilder();
                
                // start parsing lines, find separate preprocessed shaders, make a dictionary with their global + local keywords
                for (int i = 0; i < lines.Length - 1; i++)
                {
                    if (lines[i] == SeparatorLine && lines[i + 1].StartsWith(GlobalKeywordsStart, StringComparison.Ordinal))
                    {
                        variants.Add(new Variant());
                        currentVariant = variants.Last();
                        currentVariant.globalKeywords = lines[i + 1].Substring(GlobalKeywordsStart.Length).Trim();
                        var local = lines[i + 2].Substring(LocalkeywordsStart.Length).Trim();
                        currentVariant.localKeywords  = local.Contains("<none>") ? null : local;
                        
                        // reset file section so that all lines from here are appended directly
                        var fileSection = new FileSection() {fileName = "Details", fileNameDisplay = "Details"};
                        currentFileSection = fileSection;
                        currentLineIndex = 0;
                        currentVariant.mapping.Add(fileSection);
                        
                        sb.AppendLine("New variant starts: " + currentVariant.globalKeywords);
                    }
                    else if (currentVariant != null && lines[i].StartsWith(LineStart, StringComparison.Ordinal))
                    {
                        var lineContent = lines[i].Substring(LineStart.Length);
                        var lineHasFile = lineContent.IndexOf(' ');
                        if (lineHasFile > 0)
                        {
                            var numberPart = lineContent.Substring(0, lineHasFile);
                            var filePart = lineContent.Substring(lineHasFile).Trim().Trim('"');

                            var numberIndex = int.Parse(numberPart);
                            sb.AppendLine("New file starts: " + filePart + ", line " + numberIndex);

                            var fileSection = new FileSection() {fileName = filePart, fileNameDisplay = StripProjectRelativePath(filePart)};
                            currentFileSection = fileSection;
                            currentLineIndex = numberIndex;
                            currentVariant.mapping.Add(fileSection);
                        }
                        else
                        {
                            if(int.TryParse(lineContent, out var number)) {
                                sb.AppendLine("  line " + number);
                                currentLineIndex = number;
                            }
                        }
                    }
                    // regular text line
                    else if(currentFileSection != null)
                    {
                        var isFirst = !currentFileSection.lines.Any();
                        currentFileSection.lines.Add(new LineSection()
                        {
                            lineContent = lines[i], 
                            lineIndex = currentLineIndex++,
                            fileSectionStart = isFirst ? currentFileSection.fileName : null,
                            fileNameDisplay = isFirst ? currentFileSection.fileNameDisplay : null,
                        });
                    }
                }

                Debug.Log("ShaderUtil variants: " + variantCount + ", Total variants in preprocessed file: " + variants.Count);
                
                // Write back out for debugging
                // File.WriteAllText("Temp/processingResult.txt", sb.ToString());
                //
                // var sb2 = new StringBuilder();
                // foreach (var v in variants)
                // {
                //     sb2.AppendLine("=======");
                //     v.AppendAll(sb2);
                // }
                // File.WriteAllText("Temp/restoredResult.txt", sb2.ToString());
                
                availableVariants = variants;
                
                // select first found keyword combination
                localBreadcrumbs.SetSelectedKeywords(variants.First().localKeywords, false);
                globalBreadcrumbs.SetSelectedKeywords(variants.First().globalKeywords, false);
                KeywordSelectionChanged();
            }
        }

        [Serializable]
        public class LineSection
        {
            public string lineContent;
            public int lineIndex;
            public string fileSectionStart;
            public string fileNameDisplay;
        }

        [Serializable]
        public class FileSection
        {
            public string fileName;
            public string fileNameDisplay;
            public List<LineSection> lines = new List<LineSection>();

            public void AppendAll(StringBuilder target)
            {
                target.AppendLine("# file: " + fileName);
                foreach (var line in lines)
                {
                    target.Append(line.lineIndex.ToString("000000") + ":  ");
                    target.AppendLine(line.lineContent);
                }
            }
        }

        [Serializable]
        public class Variant
        {
            public string globalKeywords;
            public string localKeywords;
            public List<FileSection> mapping = new List<FileSection>();

            public void AppendAll(StringBuilder target)
            {
                target.AppendLine("Global: " + globalKeywords);
                target.AppendLine("Local: " + localKeywords);
                foreach (var section in mapping)
                    section.AppendAll(target);
            }
        }
        
        // ReSharper disable InconsistentNaming
        private static MethodInfo OpenCompiledShader;
        private static MethodInfo GetVariantCount, GetShaderGlobalKeywords, GetShaderLocalKeywords;
        // ReSharper restore InconsistentNaming
        
        private static void CompileShader(Shader theShader, bool includeAllVariants)
        {
            // ShaderUtil.OpenCompiledShader
            if (OpenCompiledShader == null) OpenCompiledShader = typeof(ShaderUtil).GetMethod("OpenCompiledShader", BindingFlags.NonPublic | BindingFlags.Static);

            int defaultMask = (1 << Enum.GetNames(typeof(ShaderCompilerPlatform)).Length - 1);
            OpenCompiledShader?.Invoke(null, new object[] // internal static extern void OpenCompiledShader(..)
            {
                theShader, // shader
                2, // mode; 1: Current  Platform; 2: All Platforms
                defaultMask, // externPlatformsMask
                includeAllVariants, // includeAllVariants
                true, // preprocessOnly
                false // stripLineDirectives
            });
        }

        void GetShaderDetails(Shader requestedShader, out ulong shaderVariantCount, out string[] localKeywords, out string[] globalKeywords)
        {
            if (GetVariantCount == null) GetVariantCount = typeof(ShaderUtil).GetMethod("GetVariantCount", (BindingFlags) (-1));
            if (GetShaderGlobalKeywords == null) GetShaderGlobalKeywords = typeof(ShaderUtil).GetMethod("GetShaderGlobalKeywords", (BindingFlags) (-1));
            if (GetShaderLocalKeywords == null) GetShaderLocalKeywords = typeof(ShaderUtil).GetMethod("GetShaderLocalKeywords", (BindingFlags) (-1));

            if (GetVariantCount == null || GetShaderGlobalKeywords == null || GetShaderLocalKeywords == null)
            {
                shaderVariantCount = 0;
                localKeywords = null;
                globalKeywords = null;
                return;
            }
            
            shaderVariantCount = (ulong) GetVariantCount.Invoke(null, new object[] {requestedShader, false});
            localKeywords = (string[]) GetShaderLocalKeywords.Invoke(null, new object[] {requestedShader});
            globalKeywords = (string[]) GetShaderGlobalKeywords.Invoke(null, new object[] {requestedShader});
            
            // var name = $"{requestedShader.name}: ({shaderVariantCount} variants, {localKeywords.Length} local, {globalKeywords.Length} global)";
        }
        
        private string SortedKeywords(ShaderMessage msg) => msg.messageDetails.Split('\n').First().Substring("Compiling Vertex program with ".Length);
        private string ToMessageStringWithoutDetails(ShaderMessage msg) => $"[{msg.severity}] (on {msg.platform}): {Path.GetFileName(msg.file)}:{msg.line} - {msg.message}";
        private string ToMessageString(ShaderMessage msg) => $"[{msg.severity}] (on {msg.platform}): {Path.GetFileName(msg.file)}:{msg.line} - {msg.message}\n{msg.messageDetails}";
    }
}