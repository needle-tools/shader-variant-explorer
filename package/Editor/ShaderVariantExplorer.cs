#if !UNITY_2021_1_OR_NEWER
#define HAVE_LOCAL_KEYWORDS
#endif
#if UNITY_2021_2_OR_NEWER
#define VARIANT_COMPILER
#endif
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
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Needle.Rendering.Editor
{
    public class ShaderVariantExplorer : EditorWindow
    {
        public Shader shader;
        
        [MenuItem("Window/Analysis/Shader Variant Explorer")]
        static void ShowNow() => GetWindow<ShaderVariantExplorer>().Show();

        [Serializable]
        public class MessageData
        {
            // public string fullMessage;
            public string messageWithoutDetails;
            // public string messageDetails;
            public string sortedKeywords;
            public ShaderCompilerMessageSeverity messageType;
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
        private ListView errorScrollView, codeScrollView;
        public string preprocessingSearchTerm = "";
        class KeywordBreadcrumbs : ToolbarBreadcrumbs
        {
            // ReSharper disable once InconsistentNaming
            public event Action onSelectionChanged;
            
            private List<string> availableKeywords = new List<string>();
            private List<string> selectedKeywords = new List<string>();

            public List<string> SelectedKeywords => selectedKeywords;
            
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
                // BuiltinShaderDefine[] keywordsForBuildTarget = ShaderUtil.GetShaderPlatformKeywordsForBuildTarget(shaderCompilerPlatform, buildTarget, ShaderData.Pass.kNoGraphicsTier);
                
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
                if (!selectedKeywords.Any()) return "<none>";
                return string.Join(" ", selectedKeywords.OrderBy(x => x.TrimStart('_')));
            }
        }

        private KeywordBreadcrumbs globalBreadcrumbs;
#if HAVE_LOCAL_KEYWORDS
        private KeywordBreadcrumbs localBreadcrumbs;
#endif

        public ShaderCompilerPlatform selectedPlatform = ShaderCompilerPlatform.D3D;
        public BuildTarget selectedBuildTarget = BuildTarget.StandaloneWindows64;
        public bool autoCompile = false;
        
        private void OnEnable()
        {
            titleContent = new GUIContent("Shader Variant Explorer");
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
            
            toolbar.Add(shaderField);
            
            toolbar.Add(new ToolbarButton(() =>
            {
                SetViewedShader(shader);
            }) { text = "Preprocess"});
            
            toolbar.Add(new ToolbarButton(() =>
            {
                CompileShader(shader, false, false, true, selectedPlatform);
                FetchAllShaderMessages();
            }) { text = "Compile"});

            // the little dropdown that the Shader inspector shows
            // var menu = new ToolbarMenu() { text = "Platform Settings" };
            // menu.RegisterCallback<ClickEvent>(click =>
            // {
            //     var popupType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.ShaderInspectorPlatformsPopup");
            //     var popup = Activator.CreateInstance(popupType, new object[] { shader}, null);
            //     UnityEditor.PopupWindow.Show(new Rect(click.position.x, click.position.y, 0, 0), (PopupWindowContent) popup);
            // });
            // toolbar.Add(menu);
            
            VisualElement CreateEnumDropdown<T>(string label, T defaultValue, Action<T> valueChanged, Func<T> currentValue) where T : Enum
            {
                var enumOptions = Enum.GetValues(typeof(T)).Cast<T>().Distinct().ToList();
                
                // var stringToEnum = enumOptions.ToDictionary(x => x.ToString(), x => x);
                // var dropdown = new DropdownField(label, stringToEnum.Keys.ToList(), 0, null, null) { value = defaultValue.ToString() };
                // dropdown.RegisterValueChangedCallback(evt =>
                // {
                //     valueChanged(stringToEnum[evt.newValue]);
                // });
                // return dropdown;

                var drp2 = new ToolbarMenu() { text = defaultValue.ToString() };
                drp2.RegisterCallback<ClickEvent>(_ =>
                {
                    void Func2(object obj)
                    {
                        if (obj is T sel)
                        {
                            valueChanged(sel);
                            drp2.text = sel.ToString();
                        }
                    }
                    
                    var menu = new GenericMenu();
                    foreach (var opt in enumOptions)
                    {
                        menu.AddItem(new GUIContent(opt.ToString()), opt.Equals(currentValue()), Func2, opt);
                    }
                    menu.ShowAsContext();
                });
                return drp2;
            }
            
            var platformDropdown = CreateEnumDropdown("Platform", selectedPlatform, x => selectedPlatform = x, () => selectedPlatform);
            var buildTargetDropdown = CreateEnumDropdown("Build Target", selectedBuildTarget, x => selectedBuildTarget = x, () => selectedBuildTarget);
            
            toolbar.Add(platformDropdown);
            toolbar.Add(buildTargetDropdown);
            
            // var search = new ToolbarPopupSearchField();
            // toolbar.Add(search);

            root.Add(toolbar);

            var globalKeywordToolbar = new Toolbar();
            
            var allCombinationSelector = new ToolbarButton(() =>
            {
                var combinationSelectionMenu = new GenericMenu();
                foreach (var variant in availableVariants)
                {
                    var hasLocalKeywords = !string.IsNullOrEmpty(variant.localKeywords);
                    var variantString = variant.globalKeywords + (hasLocalKeywords ? " " + variant.localKeywords : "");

                    int keywordCount = 0;
                    var chars = variantString.ToCharArray();
                    for (int i = 0; i < chars.Length; i++)
                    {
                        if (chars[i] == ' ') {
                            keywordCount++;
                            if (keywordCount % 2 == 0)
                                chars[i] = '/';
                        }
                    }

                    variantString = new string(chars).Replace(" ", "  •  ");
                    combinationSelectionMenu.AddItem(new GUIContent(variantString + " _"), false, SelectVariant, variant);
                }
                combinationSelectionMenu.ShowAsContext();
            })
            {
                text = "Select Keyword Combination",
            };
            globalKeywordToolbar.Add(allCombinationSelector);
            
//             var keywordsText =
// #if HAVE_LOCAL_KEYWORDS
//                 "Global Keywords";
// #else
//                 "Keywords";
// #endif
//             globalKeywordToolbar.Add(new Label(keywordsText) { style = {width = 100}});
            globalBreadcrumbs = new KeywordBreadcrumbs();
            globalBreadcrumbs.onSelectionChanged += () => KeywordSelectionChanged(true);
            globalKeywordToolbar.Add(globalBreadcrumbs);

            var filteredCombinationsSelector = new ToolbarButton(() =>
            {
                // from ALL the possible combinations, remove the ones that are already selected here
                var allKeywordStrings = availableVariants.Select(variant =>
                {
                    var hasLocalKeywords = !string.IsNullOrEmpty(variant.localKeywords);
                    var variantString = variant.globalKeywords + (hasLocalKeywords ? " " + variant.localKeywords : "");
                    return new HashSet<string>(variantString.Split(' '));
                });
                
                // selected global/local keywords
                var selectedKeywords = new HashSet<string>(globalBreadcrumbs.SelectedKeywords);
                // TODO add for local
                
                // find the keyword hashsets that contain all the selected keywords
                var remainingChoosableKeywords = allKeywordStrings
                    .Where(x => x.IsProperSupersetOf(selectedKeywords));

                var remainingOptionsMenu = new GenericMenu();
                foreach (var set in remainingChoosableKeywords)
                {
                    remainingOptionsMenu.AddItem(new GUIContent(string.Join(" • ", set.Except(selectedKeywords)) + " _"), false, SelectVariant, set);
                }
                remainingOptionsMenu.ShowAsContext();
            })
            {
                text = "Select Filtered Combination"
            };
            globalKeywordToolbar.Add(filteredCombinationsSelector);
            
            root.Add(globalKeywordToolbar);

#if HAVE_LOCAL_KEYWORDS
            var localKeywordToolbar = new Toolbar();
            localKeywordToolbar.Add(new Label("Local Keywords ") { style = {width = 100}});
            localBreadcrumbs = new KeywordBreadcrumbs();
            localBreadcrumbs.onSelectionChanged += () => KeywordSelectionChanged(true);
            localKeywordToolbar.Add(localBreadcrumbs);
            root.Add(localKeywordToolbar);
#endif
            
            var verticalSplit = new TwoPaneSplitView(0, 60, TwoPaneSplitViewOrientation.Vertical)
            {
                style = {height = 10000}
            };
            
            errorScrollView = new ListView() {
#if HAVE_LOCAL_KEYWORDS
                itemHeight = 60,
#else
                fixedItemHeight = 60,
#endif
                makeItem = () =>
                {
                    // Debug.Log("Making Item");
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
                    var label = element.Q<Label>("Message");
                    label.text = error?.messageWithoutDetails ?? "(no message)";
                    label.style.color = error?.messageType == ShaderCompilerMessageSeverity.Error ? Color.red : Color.yellow;
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
            errorScrollView.onItemsChosen += _ =>
            {
                var msg = listViewData.messages[errorScrollView.selectedIndex];
                globalBreadcrumbs.SetSelectedKeywords(msg.sortedKeywords, true);
            };
            errorScrollView.RegisterCallback<ContextClickEvent>(_ =>
            {
                var contextMenu = new GenericMenu();
                contextMenu.AddItem(new GUIContent("Clear messages for this shader"), false, () =>
                {
                    ShaderUtil.ClearShaderMessages(shader);
                    FetchAllShaderMessages();
                });
                contextMenu.AddItem(new GUIContent("Clear all cached data for shader"), false, () =>
                {
                    ShaderUtil.ClearCachedData(shader);
                    FetchAllShaderMessages();
                });
                contextMenu.ShowAsContext();
            });
            
            tempDataSerializedObject = new SerializedObject(listViewData);
            errorScrollView.Bind(tempDataSerializedObject);

            // toolbar for preprocessing
            var preprocessingToolbar = new Toolbar();
            void SelectVariant(object userData)
            {
                if (userData is Variant variant)
                {
                    globalBreadcrumbs.SetSelectedKeywords(variant.globalKeywords, false);
#if HAVE_LOCAL_KEYWORDS
                    localBreadcrumbs.SetSelectedKeywords(variant.localKeywords, false);
#endif
                    KeywordSelectionChanged(true);
                }

                if (userData is HashSet<string> set)
                {
                    globalBreadcrumbs.SetSelectedKeywords(set.ToList(), false);
                    KeywordSelectionChanged(true);
                }
            }

            var toggleFileCollapse = new ToolbarToggle()
            {
                text = "Collapse Files",
                value = collapseLines
            };
            toggleFileCollapse.RegisterValueChangedCallback(evt =>
            {
                collapseLines = evt.newValue;
                KeywordSelectionChanged(false);
            });
            preprocessingToolbar.Add(toggleFileCollapse);
            
            var openPreprocessedFileButton = new ToolbarButton(() =>
            {
                if(File.Exists(preprocessedFilePath))
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(preprocessedFilePath, 0);
            }) {text = "Open Preprocessed File"};
            preprocessingToolbar.Add(openPreprocessedFileButton);

            var spacer = new VisualElement() { style = { flexGrow = 1 } };
            preprocessingToolbar.Add(spacer);
            var preprocessingSearch = new ToolbarSearchField() { style = { minWidth = 50, flexShrink = 50} };
            preprocessingSearch.RegisterValueChangedCallback(evt =>
            {
                preprocessingSearchTerm = evt.newValue;
                KeywordSelectionChanged(false);
            });
            preprocessingToolbar.Add(preprocessingSearch);
            
            var leftPane = new VisualElement();
            
            codeScrollView = new ListView()
            {
#if HAVE_LOCAL_KEYWORDS
                itemHeight = 20,
#else
                fixedItemHeight = 20,
#endif
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
                        unityTextAlign = TextAnchor.MiddleRight,
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
                    element.style.opacity = (error?.isPartOfSearchResults ?? true) ? 1 : 0.5f;
                },
                bindingPath = nameof(listViewData.sections),
                showBoundCollectionSize = false,
                style = {
                    flexGrow = 1,
                    minHeight = StyleKeyword.Auto,
                    maxHeight = StyleKeyword.Auto,
                }
            };
            codeScrollView.Bind(tempDataSerializedObject);

            void GetFileAndLineIndex(int selectedIndex, out string file, out int occurence, out int lineIndex)
            {
                file = "";
                occurence = 0;
                for (int index = selectedIndex; index >= 0; index--)
                {
                    if (listViewData.sections[index].fileSectionStart != null)
                    {
                        file = listViewData.sections[index].fileSectionStart;
                        // get occurence - how many times did this file appear before
                        occurence = 0;
                        for (int currentIndex = index - 1; currentIndex >= 0; currentIndex--)
                        {
                            if (listViewData.sections[currentIndex].fileSectionStart == file)
                                occurence++;
                        }
                        break;
                    }
                }
                lineIndex = listViewData.sections[selectedIndex].lineIndex;
            }
            
            codeScrollView.onItemsChosen += _ =>
            {
                GetFileAndLineIndex(codeScrollView.selectedIndex, out string file, out int _, out int lineIndex);
                Debug.Log("File: " + file + ", line: " + lineIndex);
                
                if(File.Exists(file))
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(file, lineIndex);
            };
            codeScrollView.onSelectionChange += _ =>
            {
                GetFileAndLineIndex(codeScrollView.selectedIndex, out string file, out int fileOccurence, out int lineIndex);
                // Debug.Log("File: " + file + ", line: " + lineIndex);
                selectedFile = file;
                selectedFileOccurence = fileOccurence;
                selectedLineIndex = lineIndex;
            };

#if VARIANT_COMPILER
            var rightPane = new VisualElement();
            var compilationToolbar = new Toolbar();
            var compileScrollView = new VisualElement();
            var outputLabel = new ScrollView() {name = "CompilerOutput"};

            var autoCompileToggle = new ToolbarToggle() { text = "Auto Compile", value = autoCompile };
            autoCompileToggle.RegisterValueChangedCallback(evt =>
            {
                autoCompile = evt.newValue;
                CompileSpecificVariantIfAutoCompileIsOn();
            });

            void CompileSpecificVariantIfAutoCompileIsOn()
            {
                if(autoCompile) CompileSpecificVariant();
            }
            
            void CompileSpecificVariant()
            {
                if (!shader) return;
                var keywords = globalBreadcrumbs.SelectedKeywords.ToArray();
                
                outputLabel.Clear();
                var shaderData = ShaderUtil.GetShaderData(shader);
                outputLabel.Add(new Label("Subshaders [" + shaderData.SubshaderCount + "]"));
                outputLabel.Add(new Label("Keywords:\n    " + string.Join(" • ", keywords)));
                outputLabel.Add(new Label("Platform: " + selectedPlatform + "\n" + "Build Target: " + selectedBuildTarget));
                
                for (int i = 0; i < shaderData.SubshaderCount; i++)
                {
                    var subShader = shaderData.GetSubshader(i);
                    outputLabel.Add(new Label("Subshader " + i + " [" + subShader.PassCount + " passes]") { style = { fontSize = 20, paddingTop = 14}});
                    for (int j = 0; j < subShader.PassCount; j++)
                    {
                        var pass = subShader.GetPass(j);
                        outputLabel.Add(new Label("Pass: " + (string.IsNullOrEmpty(pass.Name) ? "<none>" : pass.Name)) { style = {paddingTop = 12, fontSize = 16}});
                        var source = pass.SourceCode;
                        var foldout = new Foldout() {text = "Source Code [" + source.Length + " characters]", value = false};
                        var sourceCodeLabel = new Label(source.Substring(0, Mathf.Min(source.Length, 15000))) { style = { opacity = 0.7f }};
                        foldout.Add(sourceCodeLabel);
                        outputLabel.Add(foldout);

                        foreach(ShaderType shaderType in Enum.GetValues(typeof(ShaderType)))
                        {
                            // outputLabel.Add(new Label("<i>Trying to compile " + shaderType + "</i>"));
                            // GL and VK contain all info in the ShaderType.Vertex pass (so only Vertex returns something)
                            // Metal contains all but fragment in the ShaderType.Vertex pass (so only Vertex + Fragment return something)
                            // var compileInfo = pass.CompileVariant(shaderType, keywords, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64, GraphicsTier.Tier1);
                            var compileInfo = pass.CompileVariant(shaderType, keywords, selectedPlatform, selectedBuildTarget, GraphicsTier.Tier1);
                            if(compileInfo.Messages.Length > 0)
                                outputLabel.Add(new Label("Messages [" + compileInfo.Messages.Length + "]:\n" + string.Join("\n", compileInfo.Messages.Select(ToMessageString))) { style = { color = Color.yellow }});
                            if (compileInfo.ShaderData.Length > 0)
                            {
                                outputLabel.Add(new Label("<b>" + shaderType + "</b>"));
                                outputLabel.Add(new Label("Textures:\n" + string.Join("\n", compileInfo.TextureBindings.Select(x => x.Index + " " + x.Name + " " + x.Dim))));
                            }
                        }
                    }
                }
                FetchAllShaderMessages();
            }
            
            OnKeywordSelectionChanged -= CompileSpecificVariantIfAutoCompileIsOn;
            OnKeywordSelectionChanged += CompileSpecificVariantIfAutoCompileIsOn;
            
            // UI Composition
            
            compilationToolbar.Add(autoCompileToggle);
            compilationToolbar.Add(new ToolbarButton(CompileSpecificVariant) { text = "Compile selected keyword combination"});
            compileScrollView.Add(outputLabel);
            
            rightPane.Add(compilationToolbar);
            rightPane.Add(compileScrollView);
#endif            

            leftPane.Add(preprocessingToolbar);
            leftPane.Add(codeScrollView);
            
            verticalSplit.Add(errorScrollView);

#if VARIANT_COMPILER
            var horizontalSplit = new TwoPaneSplitView(0, 400, TwoPaneSplitViewOrientation.Horizontal);

            horizontalSplit.Add(leftPane);
            horizontalSplit.Add(rightPane);
            
            verticalSplit.Add(horizontalSplit);
#else
            verticalSplit.Add(leftPane);
#endif
            
            root.Add(verticalSplit);
            
            // actual initialization
            FetchAllShaderMessages();
        }

        private string selectedFile;
        private int selectedFileOccurence;
        private int selectedLineIndex;

        private delegate void KeywordSelectionChangedEvent();
        private static event KeywordSelectionChangedEvent OnKeywordSelectionChanged;
        
        private void KeywordSelectionChanged(bool notify)
        {
            var haveSearch = !string.IsNullOrEmpty(preprocessingSearchTerm.Trim());
            var sections = availableVariants
                .FirstOrDefault(x =>
                    {
                        // Debug.Log(x.globalKeywords + " ==> " + globalBreadcrumbs.GetSortedKeywordString());
                        var result = x.globalKeywords == globalBreadcrumbs.GetSortedKeywordString()
#if HAVE_LOCAL_KEYWORDS
                        && x.localKeywords == localBreadcrumbs.GetSortedKeywordString()
#endif
                            ;
                        return result;
                    }
                )?
                .mapping
                .SelectMany(x => x.lines)
                .Where(x => !collapseLines || x.fileSectionStart != null)
                .Where(x =>
                {
                    x.isPartOfSearchResults = !haveSearch || x.lineContent.IndexOf(preprocessingSearchTerm, StringComparison.InvariantCultureIgnoreCase) > -1;
                    return !haveSearch || x.fileSectionStart != null || x.isPartOfSearchResults;
                });
            listViewData.sections = sections?.ToList();
            tempDataSerializedObject.Update();
            
            // Debug.Log("Total number of lines in variant: " + listViewData.sections?.Count);
            
            // make sure the right ListView index is selected
            SetListViewSelection(selectedFile, selectedFileOccurence, selectedLineIndex);
            
            if(notify)
                OnKeywordSelectionChanged?.Invoke();
        }

        private string preprocessedFilePath;

        private void SetListViewSelection(string s, int occurence, int index)
        {
            if (listViewData.sections == null) return;
            var sectionList = listViewData.sections.Where(x => x.fileSectionStart == s).ToList();
            if (sectionList.Count == 0 || occurence > sectionList.Count - 1) return;
            var section = sectionList[occurence];
            var sectionIndex = listViewData.sections.IndexOf(section);

            void SetSelected()
            {
                if(listViewData.sections.Count > sectionIndex)
                {
                    // TODO figure out why this happens on 2020.3
                    try {
                        codeScrollView.SetSelection(sectionIndex);
                        codeScrollView.ScrollToItem(sectionIndex);
                    } catch(ArgumentOutOfRangeException) {}
                }
            }

            // codeScrollView.schedule.Execute(SetSelected);
            SetSelected();
            EditorApplication.delayCall += SetSelected;
        }

        [NonSerialized] private string editorRoot = null;
        [NonSerialized] private string cgIncludesRoot = null;
        [NonSerialized] private string packageCacheRoot = null;
        [NonSerialized] private string assetRoot = null;
        
        string StripProjectRelativePath(string absolutePath)
        {
            if (editorRoot == null) editorRoot = Path.GetDirectoryName(EditorApplication.applicationPath)?.Replace("\\", "/") + "/";
            if (cgIncludesRoot == null) cgIncludesRoot = Path.GetDirectoryName(EditorApplication.applicationPath)?.Replace("\\", "/") + "/Data/CGIncludes/";
            if (packageCacheRoot == null) packageCacheRoot = Path.GetDirectoryName(Application.dataPath)?.Replace("\\","/") + "/Library/PackageCache/";
            if (assetRoot == null) assetRoot = Application.dataPath + "/";
            
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
            if (absolutePath.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase)) return absolutePath.Substring(assetRoot.Length);
            
            // TODO could be a local package, we could still rewrite as Packages/ path
            return absolutePath;
        }

        void FetchAllShaderMessages()
        {
            if (!shader) return;
            
            // fetch all compilation error messages
            int shaderMessageCount = ShaderUtil.GetShaderMessageCount(shader);
            var shaderMessages = (ShaderMessage[]) null;
            if (shaderMessageCount >= 1)
                shaderMessages = ShaderUtil.GetShaderMessages(shader);

            if (shaderMessages != null)
            {
                var allMessages = shaderMessages
                    .Where(x => x.severity == ShaderCompilerMessageSeverity.Error || x.severity == ShaderCompilerMessageSeverity.Warning);
                
                listViewData.messages.Clear();
                listViewData.messages.AddRange(allMessages.Select(x => new MessageData()
                {
                    messageType = x.severity,
                    // fullMessage = ToMessageString(x),
                    messageWithoutDetails = ToMessageStringWithoutDetails(x),
                    sortedKeywords = SortedKeywords(x),
                }));
            }
            else
            {
                listViewData.messages.Clear();
            }
            tempDataSerializedObject.Update();
        }
        
        void SetViewedShader(Shader selectedShader)
        {
            shader = selectedShader;
            FetchAllShaderMessages();

            if (!shader)
            {
                Debug.LogWarning("Select a shader first.");
                return;
            }
            // fetch local and global keywords for this shader
            // get shader info
            GetShaderDetails(shader, out var variantCount, out string[] localKeywords, out string[] globalKeywords);
            
            var globalKeywordsList = globalKeywords.ToList();
            // TODO not sure why this has to be added (doesn't show in the keyword list returned by Unity); potentially others have to be added as well?
            globalKeywordsList.Add("STEREO_INSTANCING_ON");
            globalKeywordsList.Add("INSTANCING_ON");
            globalKeywordsList.Add("PROCEDURAL_INSTANCING_ON");
            
            globalBreadcrumbs.SetAvailableKeywords(globalKeywordsList);
#if HAVE_LOCAL_KEYWORDS
            localBreadcrumbs.SetAvailableKeywords(localKeywords.ToList());
#endif
            
            // fetch the entire preprocessed file
            CompileShader(shader, false, true, false, selectedPlatform);
            
            // check if file exists:
            preprocessedFilePath = "Temp/Preprocessed-" + shader.name.Replace('/', '-').Replace('\\', '-') + ".shader";
            if(File.Exists(preprocessedFilePath))
            {
                // read entire file into memory, and parse it one by one - might change between Unity versions
                var lines = File.ReadAllLines(preprocessedFilePath);
                // Debug.Log("Total Line Count: " + lines.Length);
                
                var variants = new List<Variant>();
                var currentVariant = default(Variant);
                var currentFileSection = default(FileSection);
                var currentLineIndex = 0;

                const string SeparatorLine = @"//////////////////////////////////////////////////////";
#if !HAVE_LOCAL_KEYWORDS
                const string GlobalKeywordsStart = @"Keywords: ";
#else
                const string GlobalKeywordsStart = @"Global Keywords: ";
                const string LocalkeywordsStart = @"Local Keywords: ";
#endif
                const string LineStart = @"#line ";

                var sb = new StringBuilder();
                
                var sourceShaderPath = Path.GetFullPath(AssetDatabase.GetAssetPath(shader)).Replace("\\", "/");
                if (!File.Exists(sourceShaderPath))
                    sourceShaderPath = AssetDatabase.GetAssetPath(shader); // show the AssetDB path directly
                
                // start parsing lines, find separate preprocessed shaders, make a dictionary with their global + local keywords
                for (int i = 0; i < lines.Length - 1; i++)
                {
                    if (lines[i] == SeparatorLine && lines[i + 1].StartsWith(GlobalKeywordsStart, StringComparison.Ordinal))
                    {
                        variants.Add(new Variant());
                        currentVariant = variants.Last();
                        currentVariant.globalKeywords = lines[i + 1].Substring(GlobalKeywordsStart.Length).Trim();
#if HAVE_LOCAL_KEYWORDS
                        currentVariant.localKeywords  = lines[i + 2].Substring(LocalkeywordsStart.Length).Trim();
#endif
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
                            
                            // this is the source shader file
                            if (string.IsNullOrEmpty(filePart))
                                filePart = sourceShaderPath;
                            
                            var numberIndex = int.Parse(numberPart);
                            sb.AppendLine("New file starts: " + filePart + ", line " + numberIndex);

                            var fileSection = new FileSection() {fileName = filePart, fileNameDisplay = StripProjectRelativePath(filePart)};
                            currentFileSection = fileSection;
                            currentLineIndex = numberIndex;
                            currentVariant.mapping.Add(fileSection);
                            
                            currentFileSection.lines.Add(new LineSection()
                            {
                                lineContent = "", 
                                lineIndex = currentLineIndex,
                                fileSectionStart = currentFileSection.fileName,
                                fileNameDisplay = currentFileSection.fileNameDisplay,
                            });
                        }
                        else
                        {
                            if(int.TryParse(lineContent, out var number)) {
                                sb.AppendLine("  line " + number);
                                currentLineIndex = number;
                            }
                        }
                    }
                    else if (currentVariant != null && lines[i].StartsWith("-- ", StringComparison.Ordinal) && lines[i].Contains(" shader for "))
                    {
                        var shaderType = lines[i].Substring("-- ".Length).Trim(':', ' ');
                        var fileSection = new FileSection() {fileName = sourceShaderPath, fileNameDisplay = shaderType};
                        currentFileSection = fileSection;
                        currentVariant.mapping.Add(fileSection);
                        currentFileSection.lines.Add(new LineSection()
                        {
#if UNITY_2021_1_OR_NEWER
                            lineContent = "<b>" + shaderType + "</b>",
#else
                            lineContent = ">>> " + shaderType, // no rich text support in UIElements in 2020.3
#endif
                            lineIndex = 0,
                            fileSectionStart = sourceShaderPath,
                            fileNameDisplay = shaderType
                        });
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
                
                availableVariants = variants.OrderBy(x => x.globalKeywords).ThenBy(x => x.localKeywords).ToList();
                
                // select first found keyword combination
#if HAVE_LOCAL_KEYWORDS
                localBreadcrumbs.SetSelectedKeywords(variants.First().localKeywords, false);
#endif
                globalBreadcrumbs.SetSelectedKeywords(variants.FirstOrDefault()?.globalKeywords, false);
                KeywordSelectionChanged(true);
            }
        }

        [Serializable]
        public class LineSection
        {
            public string lineContent;
            public int lineIndex;
            public string fileSectionStart;
            public string fileNameDisplay;
            public bool isPartOfSearchResults;
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

        // private static List<string> s_ShaderPlatformNames = new List<string>();
        // private static List<int> s_ShaderPlatformIndices = new List<int>();
        // private static void InitializeShaderPlatforms()
        // {
        //     if (s_ShaderPlatformNames != null && s_ShaderPlatformNames.Any())
        //         return;
        //     int compilerPlatforms = (int) (typeof(ShaderUtil).GetMethod("GetAvailableShaderCompilerPlatforms", (BindingFlags)(-1))?.Invoke(null, null) ?? 0);
        //     List<string> stringList = new List<string>();
        //     List<int> intList = new List<int>();
        //     for (int index = 0; index < 32; ++index)
        //     {
        //         if ((compilerPlatforms & 1 << index) != 0)
        //         {
        //             stringList.Add(((ShaderCompilerPlatform) index).ToString());
        //             intList.Add(index);
        //         }
        //     }
        //     s_ShaderPlatformNames = stringList;
        //     s_ShaderPlatformIndices = intList;
        // }
        
        private static void CompileShader(Shader theShader, bool includeAllVariants, bool preprocessOnly, bool stripLineDirectives, ShaderCompilerPlatform shaderCompilerPlatform)
        {
            // ShaderUtil.OpenCompiledShader
            if (OpenCompiledShader == null) OpenCompiledShader = typeof(ShaderUtil).GetMethod("OpenCompiledShader", BindingFlags.NonPublic | BindingFlags.Static);

            // 262144
            int shaderCompilerPlatformMask = (1 << Enum.GetNames(typeof(ShaderCompilerPlatform)).Length - 1);
            int mode = 2;
            if (shaderCompilerPlatform != ShaderCompilerPlatform.None)
            {
                shaderCompilerPlatformMask = 1 << (int) shaderCompilerPlatform;
                mode = 3;
            }
            
            // InitializeShaderPlatforms();
            // shaderCompilerPlatformMask = 1 << s_ShaderPlatformIndices[s_ShaderPlatformNames.IndexOf(shaderCompilerPlatform.ToString())];
            
            // Debug.Log(shaderCompilerPlatformMask);
            
            // get values from the ShaderInspectorPlatformsPopup
            // var sipp = typeof(EditorWindow).Assembly.GetType("UnityEditor.ShaderInspectorPlatformsPopup");
            // var currentMode = (int) (sipp.GetProperty("currentMode", (BindingFlags)(-1))?.GetValue(null) ?? 2);
            // var currentPlatformMask = (int) (sipp.GetProperty("currentPlatformMask", (BindingFlags)(-1))?.GetValue(null) ?? shaderCompilerPlatformMask);
            // Debug.Log(currentMode + ", " + currentPlatformMask);
            
            OpenCompiledShader?.Invoke(null, new object[] // internal static extern void OpenCompiledShader(..)
            {
                theShader, // shader
                mode, // mode; 1: Current  Platform; 2: All Platforms, 3: Custom: use externPlatformsMask
                shaderCompilerPlatformMask, // externPlatformsMask
                // currentMode,
                // currentPlatformMask,
                includeAllVariants, // includeAllVariants
                preprocessOnly, // preprocessOnly
                stripLineDirectives // stripLineDirectives
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

        private string SortedKeywords(ShaderMessage msg)
        {
            var splitMsgDetails = msg.messageDetails.Split('\n');
            if(!splitMsgDetails.Any()) return msg.messageDetails;
            var firstDetail = splitMsgDetails.First();
            const string searchString ="Compiling Vertex program with ";
            if(firstDetail.IndexOf(searchString, StringComparison.Ordinal) > 0)
                return firstDetail.Substring(searchString.Length);
            return firstDetail;
        }
        private string ToMessageStringWithoutDetails(ShaderMessage msg) => $"[{msg.severity}] (on {msg.platform}): {Path.GetFileName(msg.file)}:{msg.line} - {msg.message}";
        private string ToMessageString(ShaderMessage msg) => $"[{msg.severity}] (on {msg.platform}): {Path.GetFileName(msg.file)}:{msg.line} - {msg.message}\n{msg.messageDetails}";
    }
}