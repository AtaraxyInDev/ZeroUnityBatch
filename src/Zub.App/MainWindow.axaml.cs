using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TextMateSharp.Grammars;

namespace Zub.App
{
    public partial class MainWindow : Window
    {
        private Button _btnOpen, _btnLaunch, _btnExport, _btnImport, _btnCancel, _btnSearch, _btnSaveAll, _btnClearSearch;
        private Button _btnTypes, _btnOptions, _btnEdit, _btnMark, _btnDiscard, _btnSaveAs, _btnFind, _btnPending;
        private Button _btnAllTypes, _btnNoTypes, _btnFindNext, _btnFindPrev, _btnReplace, _btnReplaceAll, _btnFindClose;
        private MenuItem _menuExportSel, _menuExportAll, _menuImportFolder, _menuImportFile;
        private CheckBox _chkMono, _chkText, _chkTex, _chkRegex, _chkCase, _chkDeep;
        private TextBox _txtQuery, _txtLog, _txtFind, _txtReplace;
        private TextBlock _txtProject, _txtStatus, _txtCounts, _txtHint, _txtPreviewTitle, _txtPreviewInfo, _txtMode, _txtTypesLabel, _txtLoading;
        private Image _imgPreview;
        private DataGrid _grid;
        private ProgressBar _bar;
        private StackPanel _panelEmpty, _panelLoading;
        private Border _editorHost;
        private Border _findBar;
        private ScrollViewer _scrollImage;
        private Panel _imageHost;
        private Grid _gridBody;
        private Panel _emptyHint;
        private Expander _expanderLog;

        private TextEditor _editor;
        private TextMate.Installation _textMate;
        private string _jsonScope, _plainScope, _currentScope = "";

        private readonly DispatcherTimer _previewTimer;
        private readonly DispatcherTimer _filterTimer;

        private UnityProject _proj;
        private List<AssetEntry> _view = new List<AssetEntry>();
        private AssetEntry _current;
        private int _kind;                  // 0 json · 1 texto · 2 textura
        private bool _editMode;
        private bool _busy;
        private bool _suppressSelection;
        private int _previewToken;

        private readonly Dictionary<string, PendingEdit> _pending = new Dictionary<string, PendingEdit>();

        private bool _onlyPending;
        private string _searchValue = "";

        private HashSet<string> _contentIds;
        private string _contentQuery;

        private CancellationTokenSource _cts;
        private int _progressEpoch;

        // imágenes importadas pendientes, para que la vista previa muestre el cambio
        private readonly Dictionary<AssetEntry, byte[]> _imgPendiente = new();

        private sealed class PendingEdit
        {
            public AssetEntry Entry;
            public byte[] Data;
            public string Text;
        }

        public MainWindow()
        {
            InitializeComponent();

            _btnOpen = Find<Button>("BtnOpen");
            _btnLaunch = Find<Button>("BtnLaunch");
            _gridBody = Find<Grid>("GridBody");
            _emptyHint = Find<Panel>("EmptyHint");
            _btnExport = Find<Button>("BtnExport");
            _btnImport = Find<Button>("BtnImport");
            _btnCancel = Find<Button>("BtnCancel");
            _btnSearch = Find<Button>("BtnSearch");
            _btnSaveAll = Find<Button>("BtnSaveAll");
            _btnClearSearch = Find<Button>("BtnClearSearch");
            _btnTypes = Find<Button>("BtnTypes");
            _btnPending = Find<Button>("BtnPending");
            _btnOptions = Find<Button>("BtnOptions");
            _btnEdit = Find<Button>("BtnEdit");
            _btnMark = Find<Button>("BtnMark");
            _btnDiscard = Find<Button>("BtnDiscard");
            _btnSaveAs = Find<Button>("BtnSaveAs");
            _btnFind = Find<Button>("BtnFind");
            _btnFindNext = Find<Button>("BtnFindNext");
            _btnFindPrev = Find<Button>("BtnFindPrev");
            _btnReplace = Find<Button>("BtnReplace");
            _btnReplaceAll = Find<Button>("BtnReplaceAll");
            _btnFindClose = Find<Button>("BtnFindClose");
            _btnAllTypes = Find<Button>("BtnAllTypes");
            _btnNoTypes = Find<Button>("BtnNoTypes");
            _menuExportSel = Find<MenuItem>("MenuExportSel");
            _menuExportAll = Find<MenuItem>("MenuExportAll");
            _menuImportFolder = Find<MenuItem>("MenuImportFolder");
            _menuImportFile = Find<MenuItem>("MenuImportFile");
            _chkMono = Find<CheckBox>("ChkMono");
            _chkText = Find<CheckBox>("ChkText");
            _chkTex = Find<CheckBox>("ChkTex");
            _chkRegex = Find<CheckBox>("ChkRegex");
            _chkCase = Find<CheckBox>("ChkCase");
            _chkDeep = Find<CheckBox>("ChkDeep");
            _txtQuery = Find<TextBox>("TxtQuery");
            _txtLog = Find<TextBox>("TxtLog");
            _txtFind = Find<TextBox>("TxtFind");
            _txtReplace = Find<TextBox>("TxtReplace");
            _txtProject = Find<TextBlock>("TxtProject");
            _txtStatus = Find<TextBlock>("TxtStatus");
            _txtCounts = Find<TextBlock>("TxtCounts");
            _txtHint = Find<TextBlock>("TxtHint");
            _txtPreviewTitle = Find<TextBlock>("TxtPreviewTitle");
            _txtPreviewInfo = Find<TextBlock>("TxtPreviewInfo");
            _txtMode = Find<TextBlock>("TxtMode");
            _txtTypesLabel = Find<TextBlock>("TxtTypesLabel");
            _txtLoading = Find<TextBlock>("TxtLoading");
            _imgPreview = Find<Image>("ImgPreview");
            _grid = Find<DataGrid>("Grid");
            _bar = Find<ProgressBar>("Bar");
            _panelEmpty = Find<StackPanel>("PanelEmpty");
            _panelLoading = Find<StackPanel>("PanelLoading");
            _editorHost = Find<Border>("EditorHost");
            _findBar = Find<Border>("FindBar");
            _scrollImage = Find<ScrollViewer>("ScrollImage");
            _imageHost = Find<Panel>("ImageHost");
            WireImageZoom();
            _expanderLog = this.GetVisualDescendants().OfType<Expander>().FirstOrDefault();

            CreateEditor();

            _btnOpen.Click += BtnOpen_Click;
            _btnLaunch.Click += (s2, e2) => LaunchGame();

            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
            _menuExportSel.Click += (s, e) => ExportAsync(SelectedEntries().ToList());
            _menuExportAll.Click += (s, e) => ExportAsync(_view.Take(2000000).ToList());
            _menuImportFolder.Click += BtnImportFolder_Click;
            _menuImportFile.Click += BtnImportFile_Click;
            _btnCancel.Click += (s, e) => { try { _cts?.Cancel(); } catch { } };
            _btnSearch.Click += BtnSearch_Click;
            _btnClearSearch.Click += (s, e) => { _txtQuery.Text = ""; ClearContentSearch(); };
            _btnSaveAll.Click += BtnSaveAll_Click;
            _btnEdit.Click += (s, e) => ToggleEdit(!_editMode);
            _btnMark.Click += BtnMark_Click;
            _btnDiscard.Click += BtnDiscard_Click;
            _btnSaveAs.Click += BtnSaveAs_Click;
            _btnFind.Click += (s, e) => ShowFind(true);
            _btnFindClose.Click += (s, e) => ShowFind(false);
            _btnFindNext.Click += (s, e) => FindNext(true);
            _btnFindPrev.Click += (s, e) => FindNext(false);
            _btnReplace.Click += (s, e) => ReplaceOne();
            _btnReplaceAll.Click += (s, e) => ReplaceAll();
            _btnPending.Click += (s, e) => { _onlyPending = !_onlyPending; RebuildView(); UpdatePendingButton(); };
            _btnAllTypes.Click += (s, e) => { _chkMono.IsChecked = true; _chkText.IsChecked = true; _chkTex.IsChecked = true; };
            _btnNoTypes.Click += (s, e) => { _chkMono.IsChecked = false; _chkText.IsChecked = false; _chkTex.IsChecked = false; };

            _chkMono.IsCheckedChanged += (s, e) => { UpdateTypeLabel(); RebuildView(); };
            _chkText.IsCheckedChanged += (s, e) => { UpdateTypeLabel(); RebuildView(); };
            _chkTex.IsCheckedChanged += (s, e) => { UpdateTypeLabel(); RebuildView(); };

            _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _filterTimer.Tick += (s2, e2) => { _filterTimer.Stop(); RebuildView(); };

            _txtQuery.TextChanged += (s, e) =>
            {
                if (_contentIds != null) return;
                _filterTimer.Stop();
                _filterTimer.Start();
            };

            _txtQuery.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; BtnSearch_Click(null, null); }
                else if (e.Key == Key.Escape) { e.Handled = true; _txtQuery.Text = ""; ClearContentSearch(); }
            };

            _txtFind.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; FindNext(true); }
                else if (e.Key == Key.Escape) { e.Handled = true; ShowFind(false); }
            };

            _grid.SelectionChanged += (s, e) => { QueuePreview(); UpdateSelectionState(); };

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _previewTimer.Tick += (s, e) => { _previewTimer.Stop(); _ = LoadPreviewAsync(); };

            UpdateTypeLabel();
        }

        private T Find<T>(string name) where T : Control => this.FindControl<T>(name);

        // ------------------------------------------------------------- editor

        private void CreateEditor()
        {
            _editor = new TextEditor
            {
                ShowLineNumbers = true,
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 12,
                Background = Brushes.Transparent,
                Padding = new Thickness(10, 8),
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                WordWrap = false
            };

            try
            {
                var options = new RegistryOptions(ThemeName.DarkPlus);
                _textMate = _editor.InstallTextMate(options);
                _jsonScope = options.GetScopeByLanguageId("json");
            }
            catch (Exception ex)
            {
                Log("Aviso: sin resaltado de sintaxis (" + ex.Message + ")");
            }

            _editor.AddHandler(KeyDownEvent, (s, e) =>
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
                {
                    e.Handled = true;
                    ShowFind(true);
                }
                else if (e.Key == Key.Escape && _findBar.IsVisible)
                {
                    e.Handled = true;
                    ShowFind(false);
                }
            }, RoutingStrategies.Tunnel);

            _editorHost.Child = _editor;
        }

        private void ShowText(string text, bool json)
        {
            _editorHost.IsVisible = true;
            _scrollImage.IsVisible = false;
            _panelEmpty.IsVisible = false;
            _panelLoading.IsVisible = false;

            string nuevo = text ?? string.Empty;
            if (!string.Equals(_editor.Text, nuevo, StringComparison.Ordinal))
                _editor.Text = nuevo;

            try
            {
                string scope = json && _jsonScope != null ? _jsonScope : null;
                if (_textMate != null && scope != _currentScope)
                {
                    _textMate.SetGrammar(scope);
                    _currentScope = scope;
                }
            }
            catch { }

            _editor.IsReadOnly = !_editMode;
        }

        // ------------------------------------------------------------- estado

        private void Ui(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action);
        }

        private void Log(string msg)
        {
            Ui(() =>
            {
                _txtLog.Text = (_txtLog.Text ?? "") + DateTime.Now.ToString("HH:mm:ss") + "   " + msg + Environment.NewLine;
                _txtLog.CaretIndex = _txtLog.Text.Length;
            });
        }

        private void SetBusy(bool value) => Ui(() => ApplyBusy(value));

        private void ApplyBusy(bool value)
        {
            _busy = value;
            bool ready = _proj != null;

            _btnOpen.IsEnabled = !value;
            _btnSearch.IsEnabled = !value && ready;
            _btnSaveAll.IsEnabled = !value && ready && _pending.Count > 0;
            _btnExport.IsEnabled = !value && ready;
            _btnImport.IsEnabled = !value && ready;
            _btnTypes.IsEnabled = !value && ready;
            _btnOptions.IsEnabled = !value;
            _btnCancel.IsEnabled = value;
            _btnEdit.IsEnabled = !value && _current != null && _kind != 2;
            _btnMark.IsEnabled = !value && _current != null && _kind != 2;
            _btnFind.IsEnabled = !value && _current != null && _kind != 2;
            _btnDiscard.IsEnabled = !value && _current != null && IsPending(_current);
            _btnSaveAs.IsEnabled = !value && _current != null && _kind != 2;
            _bar.IsVisible = value;
        }

        private void SetProgress(int percent, string text = null) => Ui(() =>
        {
            _bar.IsVisible = percent >= 0;
            _bar.IsIndeterminate = percent < 0;
            if (percent >= 0) _bar.Value = percent;
            if (text != null) _txtStatus.Text = text;
        });

        private Action<int, string> ProgressFor(int epoch) => (percent, text) => Ui(() =>
        {
            if (epoch != _progressEpoch) return;
            _bar.IsVisible = percent >= 0;
            _bar.IsIndeterminate = percent < 0;
            if (percent >= 0) _bar.Value = percent;
            if (text != null) _txtStatus.Text = text;
        });

        private void UpdateTypeLabel()
        {
            int n = (_chkMono.IsChecked == true ? 1 : 0) + (_chkText.IsChecked == true ? 1 : 0) + (_chkTex.IsChecked == true ? 1 : 0);
            _txtTypesLabel.Text = n == 0 ? "Tipos" : n == 3 ? "Tipos: todos" : $"Tipos: {n}";
        }

        private bool IsTypeEnabled(int classId)
        {
            if (classId == AssetTypes.MonoBehaviour) return _chkMono.IsChecked == true;
            if (classId == AssetTypes.TextAsset) return _chkText.IsChecked == true;
            if (classId == AssetTypes.Texture2D) return _chkTex.IsChecked == true;
            return false;
        }

        private List<AssetEntry> SelectedEntries() =>
            _grid.SelectedItems?.OfType<AssetEntry>().ToList() ?? new List<AssetEntry>();

        private static string EntryKey(AssetEntry e) => e.ContainerPath + "|" + e.PathId;

        private bool IsPending(AssetEntry e) => e != null && _pending.ContainsKey(EntryKey(e));

        private void UpdateSelectionState()
        {
            int n = SelectedEntries().Count;
            _txtHint.Text = n == 0 ? "" : n == 1 ? "1 seleccionado" : $"{n} seleccionados";
        }

        private void QueuePreview()
        {
            if (_suppressSelection) return;
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            try
            {
                var items = e.Data.GetFiles();
                var first = items?.FirstOrDefault();
                string path = first?.TryGetLocalPath();

                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Log("Arrastrado: " + path);
                    await OpenProjectAsync(path);
                }
                else if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    string dir = Path.GetDirectoryName(path);
                    await OpenProjectAsync(dir);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR al arrastrar: " + ex.Message);
            }
        }

        /// <summary>Ejecuta el juego para probar los cambios.</summary>
        private void LaunchGame()
        {
            try
            {
                if (_proj == null)
                {
                    _txtStatus.Text = "Abre primero un juego";
                    return;
                }

                string dir = _proj.DataDir;
                if (dir.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
                    dir = Path.GetDirectoryName(dir) ?? dir;

                // Android (apktool): la carpeta del proyecto contiene AndroidManifest.xml
                string android = null;
                var d = new DirectoryInfo(dir);
                for (int i = 0; i < 4 && d != null; i++, d = d.Parent)
                    if (File.Exists(Path.Combine(d.FullName, "AndroidManifest.xml")))
                        android = d.FullName;

                if (android != null)
                {
                    _txtStatus.Text = "Es un port Android: instálalo con adb para probarlo";
                    Log("Android: recompila el APK (apktool b) e instálalo con adb install.");
                    return;
                }

                var exes = Directory.GetFiles(dir, "*.exe")
                    .Where(f => !Path.GetFileName(f).StartsWith("ZeroUnity", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .ToList();

                if (exes.Count == 0)
                {
                    _txtStatus.Text = "No encuentro el .exe del juego";
                    return;
                }

                Process.Start(new ProcessStartInfo(exes[0]) { WorkingDirectory = dir, UseShellExecute = true });
                Log("Jugando: " + Path.GetFileName(exes[0]));
                _txtStatus.Text = "Juego lanzado";
            }
            catch (Exception ex)
            {
                Log("ERROR al lanzar: " + ex.Message);
            }
        }

        // -------------------------------------------------------------- android

        /// <summary>Carpeta raíz del APK descomprimido (la que tiene AndroidManifest.xml).</summary>
        private string ProjectRoot()
        {
            if (_proj == null)
                return null;

            var d = new DirectoryInfo(_proj.DataDir);
            for (int i = 0; i < 5 && d != null; i++, d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "AndroidManifest.xml")))
                    return d.FullName;

            return Path.GetDirectoryName(_proj.DataDir);
        }

        private async void MenuTools_Click(object sender, RoutedEventArgs e)
        {
            AndroidTools.Detect();
            Log("Herramientas de Android:");
            foreach (var linea in AndroidTools.Resumen().Split('\n'))
                Log("   " + linea.Trim());

            await ToolsDialog.ShowAsync(this);
        }

        private async void MenuApk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var archivos = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Elegir APK",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("APK") { Patterns = new[] { "*.apk" } }
                    }
                });

                string apk = archivos?.FirstOrDefault()?.TryGetLocalPath();
                if (string.IsNullOrEmpty(apk))
                    return;

                string carpeta = Path.Combine(Path.GetDirectoryName(apk) ?? ".",
                    Path.GetFileNameWithoutExtension(apk));

                bool ok = await Dialogs.ConfirmAsync(this, "Descomprimir APK",
                    $"Se extraerá en:\r\n{carpeta}\r\n\r\nPuede tardar varios minutos en APKs grandes.");
                if (!ok)
                    return;

                SetBusy(true);
                _txtStatus.Text = "Descomprimiendo APK…";

                bool hecho = await Task.Run(() => AndroidTools.ExtractApk(apk, carpeta, l => Ui(() => Log(l))));
                SetBusy(false);

                if (!hecho)
                {
                    await Dialogs.InfoAsync(this, "Descomprimir APK", "Falló. Revisa el registro.");
                    return;
                }

                await OpenProjectAsync(carpeta);
            }
            catch (Exception ex)
            {
                SetBusy(false);
                Log("ERROR al descomprimir: " + ex.Message);
            }
        }

        private async void MenuDlls_Click(object sender, RoutedEventArgs e)
        {
            string raiz = ProjectRoot();
            if (raiz == null)
            {
                await Dialogs.InfoAsync(this, "DLLs", "Abre primero un juego de Android.");
                return;
            }

            SetBusy(true);
            _txtStatus.Text = "Generando DLLs…";

            bool ok = await Task.Run(() => AndroidTools.BuildDlls(raiz, l => Ui(() => Log(l))));
            SetBusy(false);

            if (!ok)
            {
                await Dialogs.InfoAsync(this, "DLLs", "No se pudieron generar. Revisa el registro.");
                return;
            }

            Log("Recargando el proyecto con las DLLs nuevas…");
            await OpenProjectAsync(raiz);
        }

        private async void MenuPack_Click(object sender, RoutedEventArgs e)
        {
            string raiz = ProjectRoot();
            if (raiz == null)
            {
                await Dialogs.InfoAsync(this, "Empaquetar", "Abre primero un juego de Android.");
                return;
            }

            if (_pending.Count > 0)
            {
                bool guardar = await Dialogs.ConfirmAsync(this, "Empaquetar APK",
                    $"Tienes {_pending.Count:N0} cambios sin guardar. ¿Empaquetar de todos modos?");
                if (!guardar)
                    return;
            }

            var archivo = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "APK de salida",
                SuggestedFileName = Path.GetFileName(raiz) + "-firmado.apk",
                DefaultExtension = "apk"
            });

            string salida = archivo?.TryGetLocalPath();
            if (string.IsNullOrEmpty(salida))
                return;

            SetBusy(true);
            _txtStatus.Text = "Empaquetando y firmando…";

            string resultado = await Task.Run(() => AndroidTools.Repack(raiz, salida, l => Ui(() => Log(l))));
            SetBusy(false);

            if (resultado == null)
                await Dialogs.InfoAsync(this, "Empaquetar", "Falló. Revisa el registro.");
            else
            {
                _txtStatus.Text = "APK firmado";
                await Dialogs.InfoAsync(this, "APK listo", resultado);
            }
        }

        // -------------------------------------------------------------- abrir

        private async void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Carpeta del juego (la que contiene *_Data)",
                AllowMultiple = false
            });

            string path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrEmpty(path))
                await OpenProjectAsync(path);
        }

        private async Task OpenProjectAsync(string path)
        {
            SetBusy(true);
            SetProgress(-1, "Leyendo contenedores…");
            Ui(() => _txtLog.Text = string.Empty);

            try
            {
                _proj?.Dispose();
                _proj = null;
                _pending.Clear();

                var project = await Task.Run(() =>
                {
                    var p = new UnityProject();
                    p.Open(path, m => Log(m));
                    return p;
                });

                _proj = project;
                Ui(() => _txtProject.Text = Path.GetFileName(path.TrimEnd('\\', '/')) + "   ·   Unity " + _proj.UnityVersion);

                await Task.Run(() =>
                {
                    var all = _proj.Assets.Where(a => a.ClassId == AssetTypes.MonoBehaviour ||
                                                      a.ClassId == AssetTypes.TextAsset ||
                                                      a.ClassId == AssetTypes.Texture2D).ToList();
                    int done = 0;
                    foreach (var entry in all)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                            entry.Name = _proj.ReadName(entry, true, true);
                        done++;
                        if (done % 2500 == 0)
                            SetProgress(done * 100 / Math.Max(1, all.Count), $"Leyendo nombres {done:N0} / {all.Count:N0}…");
                    }
                });

                RebuildView();
                Ui(() =>
                {
                    _bar.IsVisible = false;
                    _txtStatus.Text = $"{_proj.Assets.Count:N0} objetos · {_proj.LoadedContainers.Count} contenedores";
                });
            }
            catch (Exception ex)
            {
                Log("ERROR al abrir: " + ex);
                Ui(() => { _bar.IsVisible = false; _txtStatus.Text = "No se pudo abrir la carpeta"; });
                await Dialogs.InfoAsync(this, "No se pudo abrir", ex.Message);
            }
            finally
            {
                SetBusy(false);
                UpdatePendingButton();
            }
        }

        private void RebuildView()
        {
            if (_proj == null) return;

            string filter = (_txtQuery.Text ?? "").Trim();
            IEnumerable<AssetEntry> query = _proj.Assets.Where(a => IsTypeEnabled(a.ClassId));

            if (_onlyPending)
                query = query.Where(a => _pending.ContainsKey(EntryKey(a)));

            if (_contentIds != null)
                query = query.Where(a => _contentIds.Contains(EntryKey(a)));

            if (filter.Length > 0 && _contentIds == null)
            {
                query = query.Where(a =>
                    (a.Name ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    a.PathId.ToString().Contains(filter) ||
                    Path.GetFileName(a.ContainerPath).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var list = query.Take(300000).ToList();

            bool teniaFoco = _txtQuery.IsFocused;

            _suppressSelection = true;
            _view = list;
            _grid.ItemsSource = new DataGridCollectionView(list);
            _suppressSelection = false;

            if (teniaFoco)
                Dispatcher.UIThread.Post(() => { if (!_txtQuery.IsFocused) _txtQuery.Focus(); });

            bool anyType = _chkMono.IsChecked == true || _chkText.IsChecked == true || _chkTex.IsChecked == true;

            if (_emptyHint != null)
            {
                _emptyHint.IsVisible = _proj == null;
                _grid.IsVisible = _proj != null;
            }

            _txtCounts.Text = !anyType
                ? "Elige uno o más tipos para listar objetos"
                : _onlyPending
                    ? $"{list.Count:N0} con cambios preparados"
                    : _contentIds != null
                        ? $"{list.Count:N0} coincidencias de «{_contentQuery}»"
                        : $"{list.Count:N0} objetos listados";

            if (!anyType)
                _txtStatus.Text = "Sin tipos seleccionados";
        }

        private void ClearContentSearch()
        {
            if (_contentIds == null) return;
            _contentIds = null;
            _contentQuery = null;
            RebuildView();
            _txtStatus.Text = "Búsqueda descartada";
        }

        // ------------------------------------------------------------ preview

        private void ToggleEdit(bool edit)
        {
            if (_current == null || _kind == 2) return;

            _editMode = edit;
            _editor.IsReadOnly = !edit;
            _btnEdit.Content = edit ? "Vista" : "Editar";
            _txtMode.Text = edit ? "Edición" : "Lectura";

            if (edit)
            {
                _editor.Focus();
                ShowFind(false);
            }
            else
            {
                ShowFind(false);
            }
        }

        private void SetLoading(bool on, string text = null)
        {
            _panelLoading.IsVisible = on;
            if (text != null) _txtLoading.Text = text;
            if (on)
            {
                _scrollImage.IsVisible = false;
                _panelEmpty.IsVisible = false;
            }
        }

        private async Task LoadPreviewAsync()
        {
            if (_proj == null || _busy) return;

            var entry = _grid.SelectedItems?.OfType<AssetEntry>().FirstOrDefault();
            int token = ++_previewToken;

            if (entry == null)
            {
                _current = null;
                _panelEmpty.IsVisible = true;
                _editorHost.IsVisible = false;
                _scrollImage.IsVisible = false;
                _panelLoading.IsVisible = false;
                _txtPreviewTitle.Text = "Vista previa";
                _txtPreviewInfo.Text = "Selecciona un objeto del listado";
                _btnMark.IsEnabled = false;
                _btnEdit.IsEnabled = false;
                _btnFind.IsEnabled = false;
                return;
            }

            bool sameEntry = _current == entry;
            _current = entry;
            _editMode = false;
            _editor.IsReadOnly = true;
            _btnEdit.Content = "Editar";
            _txtMode.Text = "Lectura";
            ShowFind(false);

            if (sameEntry && _editorHost.IsVisible && _kind != 2 && !IsPending(entry))
                return;

            if (string.IsNullOrEmpty(entry.Name))
                entry.Name = await Task.Run(() => _proj.ReadName(entry, true, true));

            _txtPreviewTitle.Text = entry.DisplayName;
            _txtPreviewInfo.Text = $"{entry.TypeText} · {Path.GetFileName(entry.ContainerPath)} · PathID {entry.PathId} · {entry.Size:N0} bytes" +
                                   (IsPending(entry) ? "   ·   cambio preparado" : "");

            if (entry.ClassId == AssetTypes.Texture2D)
            {
                _kind = 2;
                SetLoading(true, "Decodificando textura…");
                try
                {
                    if (_imgPendiente.TryGetValue(entry, out var pngNuevo))
                    {
                        using var msN = new MemoryStream(pngNuevo);
                        _imgPreview.Source = new Bitmap(msN);
                        _scrollImage.IsVisible = true;
                        FitImage();
                        SetLoading(false);
                        _txtPreviewInfo.Text += "   ·   imagen nueva preparada";
                        _btnEdit.IsEnabled = false;
                        _btnMark.IsEnabled = false;
                        _btnFind.IsEnabled = false;
                        return;
                    }

                    var t = await Task.Run(() => TextureReader.Preview(_proj, entry, 1024));
                    if (token != _previewToken) return;

                    SetLoading(false);
                    if (t.Decodable && t.Png != null)
                    {
                        using var ms = new MemoryStream(t.Png);
                        _imgPreview.Source = new Bitmap(ms);
                        _scrollImage.IsVisible = true;
                        FitImage();
                        _txtPreviewInfo.Text += $"   ·   {t.Width}×{t.Height}   ·   {t.Format}" +
                                                (t.MipCount > 1 ? $"   ·   {t.MipCount} mips" : "");
                    }
                    else
                    {
                        ShowText("Sin previsualización disponible.\r\n\r\n" + t.Error + "\r\nFormato: " + t.Format, false);
                        _txtPreviewInfo.Text += "   ·   " + t.Format;
                    }
                }
                catch (Exception ex)
                {
                    SetLoading(false);
                    ShowText("Error: " + ex.Message, false);
                }
                finally
                {
                    _btnEdit.IsEnabled = false;
                    _btnMark.IsEnabled = false;
                    _btnFind.IsEnabled = false;
                    _btnDiscard.IsEnabled = IsPending(entry);
                    _btnSaveAs.IsEnabled = false;
                }
                return;
            }

            _kind = entry.ClassId == AssetTypes.MonoBehaviour ? 0 : 1;

            bool big = entry.Size > 400_000;
            SetLoading(true, big ? $"Leyendo objeto grande ({entry.Size:N0} bytes)…" : "Leyendo objeto…");

            try
            {
                string text = await Task.Run(() => ReadPendingOrFile(entry));
                if (token != _previewToken) return;
                ShowText(text, _kind == 0);
                HighlightFirstMatch();
            }
            catch (Exception ex)
            {
                if (token != _previewToken) return;
                ShowText("Error: " + ex.Message, false);
            }
            finally
            {
                if (token == _previewToken)
                {
                    _panelLoading.IsVisible = false;
                    _btnEdit.IsEnabled = true;
                    _btnMark.IsEnabled = true;
                    _btnFind.IsEnabled = true;
                    _btnDiscard.IsEnabled = IsPending(entry);
                    _btnSaveAs.IsEnabled = true;
                }
            }
        }

        /// <summary>Salta a la primera aparición del texto buscado dentro del objeto.</summary>
        private void HighlightFirstMatch()
        {
            if (string.IsNullOrEmpty(_searchValue) || _kind == 2 || _editor == null)
                return;

            try
            {
                string text = _editor.Text ?? "";
                int at = text.IndexOf(_searchValue, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    return;

                int len = _searchValue.Length;
                int line = _editor.Document.GetLineByOffset(at).LineNumber;

                _editor.TextArea.Caret.Offset = at;
                _editor.Select(at, len);

                void Saltar(object s2, EventArgs e2)
                {
                    _editor.LayoutUpdated -= Saltar;
                    try
                    {
                        double alto = _editor.TextArea.TextView.DefaultLineHeight;
                        if (alto <= 0) alto = 16;

                        var sv = _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                        if (sv != null)
                            sv.Offset = new Vector(sv.Offset.X, Math.Max(0, (line - 3) * alto));

                        _editor.TextArea.Caret.Offset = at;
                        _editor.Select(at, len);

                    }
                    catch (Exception ex)
                    {
                        Log("[salto] " + ex.Message);
                    }
                }

                _editor.LayoutUpdated += Saltar;
                _editor.UpdateLayout();

                _txtPreviewInfo.Text += "   -   '" + _searchValue + "' en la linea " + line;
            }
            catch { }
        }

        /// <summary>Si el objeto tiene un cambio preparado, se busca en ese texto y no en el disco.</summary>
        private string PendingText(AssetEntry entry)
        {
            return _pending.TryGetValue(EntryKey(entry), out var pend) ? (pend.Text ?? "") : null;
        }

        private string ReadPendingOrFile(AssetEntry entry)
        {
            if (_pending.TryGetValue(EntryKey(entry), out var pend))
                return pend.Text ?? "";

            if (entry.ClassId == AssetTypes.TextAsset)
            {
                byte[] data = ContentReader.ReadTextAssetBytes(_proj, entry, out _, out string err);
                if (data == null)
                    return "(no se pudo leer: " + err + ")";
                return ContentReader.ToDisplayText(data);
            }

            return ContentReader.ReadMonoJson(_proj, entry);
        }

        // -------------------------------------------------- buscar/reemplazar

        private void ShowFind(bool show)
        {
            _findBar.IsVisible = show;
            if (show)
            {
                if (_kind == 2) { _findBar.IsVisible = false; return; }
                _txtFind.Focus();
                _txtFind.SelectAll();
            }
            else
            {
                _editor.Focus();
            }
        }

        private void FindNext(bool forward)
        {
            string needle = _txtFind.Text ?? "";
            if (needle.Length == 0 || _editor == null) return;

            string text = _editor.Text ?? "";
            int from = _editor.SelectionStart + (forward ? Math.Max(1, _editor.SelectionLength) : 0);

            int index = -1;
            if (forward)
            {
                index = text.IndexOf(needle, Math.Min(from, text.Length), StringComparison.OrdinalIgnoreCase);
                if (index < 0) index = text.IndexOf(needle, 0, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                int start = Math.Max(0, _editor.SelectionStart - 1);
                index = start <= 0 ? -1 : text.LastIndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) index = text.LastIndexOf(needle, StringComparison.OrdinalIgnoreCase);
            }

            if (index < 0)
            {
                _txtStatus.Text = $"«{needle}» no aparece en este objeto";
                return;
            }

            _editor.Select(index, needle.Length);
            _editor.ScrollToLine(_editor.Document.GetLineByOffset(index).LineNumber);
            _txtStatus.Text = $"«{needle}» en la posición {index:N0}";
        }

        private void ReplaceOne()
        {
            if (_editor.IsReadOnly)
            {
                _txtStatus.Text = "Pulsa Editar para modificar el contenido";
                return;
            }

            string needle = _txtFind.Text ?? "";
            if (needle.Length == 0) return;

            string selection = _editor.SelectedText ?? "";
            if (selection.Equals(needle, StringComparison.OrdinalIgnoreCase))
            {
                int at = _editor.SelectionStart;
                _editor.Document.Replace(at, _editor.SelectionLength, _txtReplace.Text ?? "");
                _editor.Select(at + (_txtReplace.Text ?? "").Length, 0);
            }
            else
            {
                FindNext(true);
            }
        }

        private void ReplaceAll()
        {
            if (_editor.IsReadOnly)
            {
                _txtStatus.Text = "Pulsa Editar para modificar el contenido";
                return;
            }

            string needle = _txtFind.Text ?? "";
            if (needle.Length == 0) return;

            string text = _editor.Text ?? "";
            int count = 0, idx = 0;
            var sb = new StringBuilder();
            while (true)
            {
                int hit = text.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase);
                if (hit < 0) { sb.Append(text, idx, text.Length - idx); break; }
                sb.Append(text, idx, hit - idx);
                sb.Append(_txtReplace.Text ?? "");
                idx = hit + needle.Length;
                count++;
            }

            if (count == 0)
            {
                _txtStatus.Text = $"«{needle}» no aparece en este objeto";
                return;
            }

            _editor.Text = sb.ToString();
            _txtStatus.Text = $"{count:N0} reemplazos";
        }

        // ------------------------------------------------- cambios preparados

        private void BtnMark_Click(object sender, RoutedEventArgs e)
        {
            if (_proj == null || _current == null || _kind == 2) return;

            var entry = _current;
            string text = _editor.Text ?? "";

            try
            {
                byte[] data;
                string error = null;

                if (_kind == 0)
                    data = ContentReader.BuildMonoBytes(_proj, entry, text, out error);
                else
                    data = ContentReader.BuildTextAsset(_proj, entry, Encoding.UTF8.GetBytes(text), out error);

                if (data == null)
                {
                    _txtStatus.Text = "No se pudo preparar: " + (error ?? "contenido inválido");
                    Log("✗ " + entry.DisplayName + ": " + error);
                    return;
                }

                _pending[EntryKey(entry)] = new PendingEdit { Entry = entry, Data = data, Text = text };
                entry.Pending = true;

                _btnDiscard.IsEnabled = true;
                _txtPreviewInfo.Text = $"{entry.TypeText} · {Path.GetFileName(entry.ContainerPath)} · PathID {entry.PathId} · cambio preparado";
                _txtStatus.Text = $"Cambio preparado: {entry.DisplayName}";
                Log("• preparado: " + entry.DisplayName + $" ({data.Length:N0} bytes)");
                UpdatePendingButton();
            }
            catch (Exception ex)
            {
                Log("ERROR al preparar: " + ex);
            }
        }

        private void BtnDiscard_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;

            _pending.Remove(EntryKey(_current));
            _current.Pending = false;
            _btnDiscard.IsEnabled = false;
            Log("descartado: " + _current.DisplayName);
            _txtStatus.Text = "Cambio descartado";
            UpdatePendingButton();
            _ = LoadPreviewAsync();
        }

        private void UpdatePendingButton()
        {
            Ui(() =>
            {
                int n = _pending.Count;
                _btnSaveAll.Content = $"Guardar todo ({n})";
                _btnSaveAll.Classes.Set("accent", n > 0);
                _btnSaveAll.IsEnabled = n > 0 && !_busy;

                _btnPending.Content = $"Modificados ({n})";
                _btnPending.Classes.Set("accent", _onlyPending);
                _btnPending.Classes.Set("mode", true);
            });
        }

        private async void BtnSaveAll_Click(object sender, RoutedEventArgs e)
        {
            if (_proj == null || _pending.Count == 0) return;

            int total = _pending.Count;
            bool ok = await Dialogs.ConfirmAsync(this, "Guardar cambios",
                $"Se escribirán {total:N0} objetos en los archivos del juego:\r\n\r\n{_proj.DataDir}\r\n\r\n" +
                "Cada contenedor se reescribe una sola vez, se verifica byte a byte y se deja copia .zub_bak la primera vez.");
            if (!ok) return;

            await SaveAllAsync();
        }

        /// <summary>Escribe todos los cambios preparados (sin preguntar).</summary>
        internal async Task SaveAllAsync()
        {
            if (_proj == null || _pending.Count == 0)
                return;

            int total = _pending.Count;

            _previewTimer.Stop();
            _cts = new CancellationTokenSource();
            int epoch = ++_progressEpoch;
            var progress = ProgressFor(epoch);
            SetBusy(true);
            progress(0, $"Escribiendo 0 / {total:N0}…");

            try
            {
                var edits = _pending.Values
                    .Select(p => new RawEdit { Entry = p.Entry, Data = p.Data, Label = p.Entry.DisplayName })
                    .ToList();

                var groups = edits.GroupBy(x => x.Entry.ContainerPath).ToList();
                int applied = 0, unchanged = 0, failed = 0, idx = 0;
                var written = new List<AssetEntry>();
                var sw = Stopwatch.StartNew();

                foreach (var group in groups)
                {
                    if (_cts.IsCancellationRequested) { Log("Cancelado."); break; }

                    idx++;
                    progress(idx * 100 / groups.Count, $"Escribiendo {idx}/{groups.Count} · {Path.GetFileName(group.Key)}");

                    var list = group.ToList();
                    var reports = await Task.Run(() => BatchImporter.ApplyRaw(_proj, list, true, true));

                    foreach (var rep in reports)
                    {
                        applied += rep.Applied;
                        unchanged += rep.Unchanged;
                        failed += rep.Failed;
                        Log($"→ {Path.GetFileName(rep.Container)} · {rep.Summary}");
                        foreach (var err in rep.Errors.Take(4))
                            Log("   " + err);
                    }
                    written.AddRange(list.Select(x => x.Entry));
                }

                Ui(() =>
                {
                    foreach (var entry in written)
                    {
                        if (_pending.Remove(EntryKey(entry)))
                            entry.Pending = false;
                    }
                });

                sw.Stop();
                Log($"TOTAL · {applied:N0} escritos · {unchanged:N0} sin cambios · {failed:N0} fallos · {sw.Elapsed.TotalSeconds:F1}s");

                _progressEpoch++;
                _bar.Value = 0;
                _txtStatus.Text = $"Guardado · {applied:N0} objetos en {sw.Elapsed.TotalSeconds:F0}s";

                if (failed > 0)
                    await Dialogs.InfoAsync(this, "Guardar cambios",
                        $"Se escribieron {applied:N0} objetos y fallaron {failed:N0}. Revisa el registro.");
            }
            catch (Exception ex)
            {
                Log("ERROR al guardar: " + ex);
                _txtStatus.Text = "Error al guardar";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SetBusy(false);
                UpdatePendingButton();
                Ui(() => _bar.IsVisible = false);
                _ = LoadPreviewAsync();
            }
        }

        private async void BtnSaveAs_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;

            string text = _editor.Text ?? "";

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Guardar contenido",
                SuggestedFileName = _current.MatchStem + (_kind == 1 ? ".txt" : ".json"),
                DefaultExtension = _kind == 1 ? "txt" : "json"
            });

            if (file == null) return;

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(text);
            Log("Guardado: " + file.TryGetLocalPath());
        }

        // ----------------------------------------------------------- exportar

        private async Task ExportAsync(List<AssetEntry> targets)
        {
            if (_proj == null) return;

            if (targets == null || targets.Count == 0)
            {
                await Dialogs.InfoAsync(this, "Exportar", "Selecciona al menos un objeto del listado.");
                return;
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Carpeta donde exportar",
                AllowMultiple = false
            });

            string outDir = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (string.IsNullOrEmpty(outDir)) return;

            int epoch = ++_progressEpoch;
            var progress = ProgressFor(epoch);
            SetBusy(true);
            progress(0, $"Exportando 0 / {targets.Count:N0}…");

            try
            {
                var report = await Task.Run(() => BatchExporter.Export(_proj, targets, outDir,
                    (i, total, name) => progress(i * 100 / Math.Max(1, total), $"Exportando {i:N0} / {total:N0}…")));

                Log($"Exportados {report.Exported:N0} · fallos {report.Failed:N0} · {report.Seconds:F1}s → {outDir}");
                foreach (var err in report.Errors.Take(8))
                    Log("   " + err);

                _progressEpoch++;
                _txtStatus.Text = $"Exportados {report.Exported:N0} archivos en {report.Seconds:F0}s";

                if (report.Failed > 0)
                    await Dialogs.InfoAsync(this, "Exportar",
                        $"Se exportaron {report.Exported:N0} y fallaron {report.Failed:N0}.\r\n{report.Errors.FirstOrDefault()}");
            }
            catch (Exception ex)
            {
                Log("ERROR al exportar: " + ex);
            }
            finally
            {
                SetBusy(false);
                Ui(() => _bar.IsVisible = false);
            }
        }

        // -------------------------------------------------------- reimportar

        private async void BtnImportFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_proj == null) return;

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Carpeta con los archivos editados (JSON/TXT/bin)",
                AllowMultiple = false
            });

            string folder = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (string.IsNullOrEmpty(folder)) return;

            await LoadFolderAsync(folder);
        }

        private async Task LoadFolderAsync(string folder)
        {
            _previewTimer.Stop();
            _cts = new CancellationTokenSource();
            int epoch = ++_progressEpoch;
            var progress = ProgressFor(epoch);
            SetBusy(true);
            progress(-1, "Emparejando archivos…");

            try
            {
                var pool = _proj.Assets.Where(a => a.ClassId == AssetTypes.MonoBehaviour ||
                                                   a.ClassId == AssetTypes.TextAsset ||
                                                   a.ClassId == AssetTypes.Texture2D).ToList();

                var stats = await Task.Run(() => CrossBuildMatcher.Match(_proj, folder, pool,
                    (d, t) => progress(d * 100 / Math.Max(1, t), $"Emparejando {d:N0} / {t:N0}…")));

                var items = stats.Items;
                var unmatched = stats.Missing;

                Log("Emparejado: " + stats.Summary);

                if (stats.Duplicados > 0)
                    Log($"   {stats.Duplicados:N0} archivos estaban repetidos en varios bundles: se aplican a todas sus copias.");

                if (stats.ByName > 0)
                    Log($"   {stats.ByName:N0} emparejados por nombre de objeto (build distinta).");

                if (unmatched != null && unmatched.Count > 0)
                {
                    foreach (var u in unmatched.Take(5))
                        Log("   sin pareja: " + u);
                }

                if (items.Count == 0)
                {
                    _progressEpoch++;
                    _bar.IsVisible = false;
                    await Dialogs.InfoAsync(this, "Cargar cambios",
                        "Ningún archivo de esa carpeta corresponde a objetos de este juego.");
                    return;
                }

                progress(0, $"Leyendo {items.Count:N0} archivos…");
                var edits = await Task.Run(() => BatchImporter.BuildEdits(_proj, items, out var errs,
                    (d, t) => progress(d * 100 / Math.Max(1, t), $"Leyendo {d:N0} / {t:N0}…")));

                var sourceOf = new Dictionary<AssetEntry, string>();
                foreach (var it in items)
                    if (it.Entry != null && !sourceOf.ContainsKey(it.Entry))
                        sourceOf[it.Entry] = it.SourceFile;

                AddPending(edits, sourceOf);

                // si el objeto cargado en el panel es uno de los modificados, refrescar la vista
                if (_current != null && edits.Any(x => x.Entry == _current))
                    await LoadPreviewAsync();

                _progressEpoch++;
                _bar.IsVisible = false;
                _txtStatus.Text = $"{_pending.Count:N0} en cola ({edits.Count:N0} ahora) · nada escrito";
                Log($"Cargados {edits.Count:N0}. En cola ahora: {_pending.Count:N0}. Nada escrito hasta pulsar «Guardar todo».");
                UpdatePendingButton();
                RebuildView();
            }
            catch (Exception ex)
            {
                Log("ERROR al cargar: " + ex);
                _txtStatus.Text = "Error al cargar los archivos";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SetBusy(false);
                Ui(() => _bar.IsVisible = false);
            }
        }

        /// <summary>Guarda los cambios en la cola: no se escribe nada hasta pulsar Guardar todo.</summary>
        private void AddPending(IEnumerable<RawEdit> edits, IDictionary<AssetEntry, string> sourceOf)
        {
            foreach (var edit in edits)
            {
                if (edit?.Entry == null || edit.Data == null)
                    continue;

                string text = "";
                try
                {
                    if (sourceOf != null && sourceOf.TryGetValue(edit.Entry, out string src) && File.Exists(src))
                    {
                        text = src.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                            ? ContentReader.ToDisplayText(File.ReadAllBytes(src))
                            : File.ReadAllText(src);
                    }
                }
                catch { }

                _pending[EntryKey(edit.Entry)] = new PendingEdit
                {
                    Entry = edit.Entry,
                    Data = edit.Data,
                    Text = text
                };
                edit.Entry.Pending = true;
            }
        }

        private async void BtnImportFile_Click(object sender, RoutedEventArgs e)
        {
            if (_proj == null) return;

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Archivo con el cambio (JSON, texto o imagen)",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Cambios (JSON, texto, imagen)")
                        { Patterns = new[] { "*.json", "*.txt", "*.bin", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga" } },
                    new FilePickerFileType("Todos los archivos") { Patterns = new[] { "*" } }
                }
            });

            if (files.Count == 0) return;

            int epoch = ++_progressEpoch;
            var progress = ProgressFor(epoch);
            SetBusy(true);
            progress(-1, "Leyendo archivo…");

            try
            {
                var pool = _proj.Assets.Where(a => a.ClassId == AssetTypes.MonoBehaviour ||
                                                   a.ClassId == AssetTypes.TextAsset ||
                                                   a.ClassId == AssetTypes.Texture2D).ToList();

                int added = 0, unmatched = 0;
                var edits = new List<RawEdit>();
                var sourceOf = new Dictionary<AssetEntry, string>();

                foreach (var f in files)
                {
                    string path = f.TryGetLocalPath();
                    if (string.IsNullOrEmpty(path)) continue;

                    var entry = BatchImporter.MatchOne(path, pool);

                    // sin PathID en el nombre: solo si hay UN objeto seleccionado
                    if (entry == null)
                    {
                        int seleccion = _grid.SelectedItems?.Count ?? 0;

                        if (seleccion == 1 && _current != null &&
                            (_current.ClassId == AssetTypes.Texture2D ||
                             _current.ClassId == AssetTypes.TextAsset ||
                             _current.ClassId == AssetTypes.MonoBehaviour))
                        {
                            entry = _current;
                        }
                        else if (seleccion > 1)
                        {
                            Log("   " + Path.GetFileName(path) + ": el nombre no lleva PathID y hay " +
                                seleccion + " objetos seleccionados; se omite.");
                        }
                    }

                    if (entry == null) { unmatched++; continue; }

                    var item = new ImportItem { Entry = entry, SourceFile = path };
                    var list = await Task.Run(() => BatchImporter.BuildEdits(_proj, new[] { item }, out _));

                    foreach (var edit in list)
                    {
                        edits.Add(edit);
                        sourceOf[edit.Entry] = path;
                        added++;

                        if (edit.Entry.ClassId == AssetTypes.Texture2D)
                        {
                            try { _imgPendiente[edit.Entry] = File.ReadAllBytes(path); } catch { }
                        }
                    }
                }

                if (added == 0)
                {
                    _progressEpoch++;
                    _bar.IsVisible = false;
                    await Dialogs.InfoAsync(this, "Cargar cambios",
                        "No se pudo interpretar el archivo.\r\nUsa archivos exportados por esta herramienta " +
                        "(nombre-contenedor-PathID.json) o por UABEA.");
                    return;
                }

                AddPending(edits, sourceOf);

                // si el objeto cargado en el panel es uno de los modificados, refrescar la vista
                if (_current != null && edits.Any(x => x.Entry == _current))
                    await LoadPreviewAsync();

                _progressEpoch++;
                _bar.IsVisible = false;
                _txtStatus.Text = $"{added:N0} cambios preparados" + (unmatched > 0 ? $" · {unmatched} sin pareja" : "");
                Log($"Cargados {added:N0} cambios. Nada escrito todavía.");
                UpdatePendingButton();
                RebuildView();
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex);
            }
            finally
            {
                SetBusy(false);
                Ui(() => _bar.IsVisible = false);
            }
        }

        private void SetProgressIndeterminate(string text)
        {
            int epoch = ++_progressEpoch;
            ProgressFor(epoch)(-1, text);
        }

        // ------------------------------------------------------------ búsqueda

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            if (_proj == null) return;
            // el Enter del cuadro de texto tambien llega aqui: dos busquedas a la vez
            // comparten los readers de los serializados y revientan el programa
            if (_searchRunning) return;
            _searchRunning = true;
            try
            {
                await RunSearchAsync();
            }
            finally
            {
                _searchRunning = false;
            }
        }

        private async Task RunSearchAsync()
        {

            string query = (_txtQuery.Text ?? "").Trim();
            if (query.Length == 0) { ClearContentSearch(); return; }

            _previewTimer.Stop();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            int epoch = ++_progressEpoch;
            var progress = ProgressFor(epoch);
            SetBusy(true);
            progress(-1, "Buscando…");

            try
            {
                var pool = _proj.Assets.Where(a => IsTypeEnabled(a.ClassId)).ToList();

                if (pool.Count == 0)
                {
                    progress(0, "Elige al menos un tipo antes de buscar");
                    return;
                }

                bool useRegex = _chkRegex.IsChecked == true;
                bool ignoreCase = _chkCase.IsChecked != true;
                bool allowDeep = _chkDeep.IsChecked == true;

                SearchEngine.ParseQuery(query, out string field, out string value);

                var sw = Stopwatch.StartNew();
                List<SearchHit> hits;
                string note = "";

                if (useRegex)
                {
                    hits = await Task.Run(() => SearchEngine.Search(_proj, pool, query, true, ignoreCase, 3000,
                        8L * 1024 * 1024,
                        (d, t) => progress(d * 100 / Math.Max(1, t), $"Buscando {d:N0} / {t:N0}…"), token,
                    PendingText));
                }
                else if (field.Length > 0 && value.Length > 0)
                {
                    // campo + valor: el valor existe en los bytes, el campo en la plantilla del script
                    progress(-1, $"Buscando «{value}»…");
                    hits = await Task.Run(() => SearchEngine.Search(_proj, pool, value, false, ignoreCase, 6000,
                        8L * 1024 * 1024,
                        (d, t) => progress(d * 100 / Math.Max(1, t), $"Buscando {d:N0} / {t:N0}…"), token,
                    PendingText));

                    _searchValue = value;
                    progress(-1, $"Comprobando el campo {field} en {hits.Count:N0} objetos…");
                    hits = await Task.Run(() => SearchEngine.FilterByField(_proj, hits, field));
                    note = $" · campo {field}";
                }
                else if (field.Length > 0)
                {
                    progress(-1, $"Buscando el campo {field}…");
                    hits = await Task.Run(() => SearchEngine.SearchDeep(_proj, pool, field, ignoreCase, 3000,
                        (d, t) => progress(d * 100 / Math.Max(1, t), $"Analizando {d:N0} / {t:N0} scripts…"), token));
                    note = " · estructura";
                }
                else
                {
                    _searchValue = value;
                    hits = await Task.Run(() => SearchEngine.Search(_proj, pool, value, false, ignoreCase, 3000,
                        8L * 1024 * 1024,
                        (d, t) => progress(d * 100 / Math.Max(1, t), $"Buscando {d:N0} / {t:N0}…"), token,
                    PendingText));

                    if (hits.Count == 0 && allowDeep)
                    {
                        progress(-1, "Sin resultados en bytes · buscando campos…");
                        hits = await Task.Run(() => SearchEngine.SearchDeep(_proj, pool, value, ignoreCase, 3000,
                            (d, t) => progress(d * 100 / Math.Max(1, t), $"Analizando {d:N0} / {t:N0} scripts…"), token));
                        note = " · estructura";
                    }
                }

                sw.Stop();

                if (hits.Count == 0)
                {
                    _progressEpoch++;
                    _bar.IsVisible = false;
                    _txtStatus.Text = $"0 coincidencias de «{query}» · el listado no se ha modificado";
                    Log($"«{query}» → 0 resultados");
                    return;
                }

                var ids = new HashSet<string>();
                foreach (var hit in hits)
                    ids.Add(EntryKey(hit.Entry));

                _contentIds = ids;
                _contentQuery = query + note;
                RebuildView();

                _progressEpoch++;
                _bar.IsVisible = false;
                _txtStatus.Text = $"{ids.Count:N0} coincidencias en {sw.Elapsed.TotalSeconds:F2}s{note}";
                Log($"«{query}» → {ids.Count:N0} objetos en {sw.Elapsed.TotalSeconds:F2}s{note} (sobre {pool.Count:N0})");
            }
            catch (OperationCanceledException)
            {
                _progressEpoch++;
                _txtStatus.Text = "Búsqueda cancelada";
            }
            catch (Exception ex)
            {
                Log("ERROR en búsqueda: " + ex);
                _progressEpoch++;
                _txtStatus.Text = "Error en la búsqueda";
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SetBusy(false);
                Ui(() => _bar.IsVisible = false);
            }
        }

        // -------------------------------------------------------------- otros

        private async Task OpenAndMaybeSearchAsync(string path)
        {
            await OpenProjectAsync(path);

            if (!string.IsNullOrEmpty(Program.AutoImport))
            {
                _chkMono.IsChecked = true;
                _chkText.IsChecked = true;
                await Task.Delay(400);
                await LoadFolderAsync(Program.AutoImport);
                await Task.Delay(2000);

                if (Program.AutoSave)
                {
                    await SaveAllAsync();
                    await Task.Delay(1500);
                }

                if (string.IsNullOrEmpty(Program.AutoSearch))
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "zub_selftest.log"), _txtLog.Text ?? "");
                    return;
                }
            }

            if (string.IsNullOrEmpty(Program.AutoSearch)) return;

            try
            {
                string dump = Path.Combine(Path.GetTempPath(), "zub_selftest.log");
                _chkMono.IsChecked = true;
                _chkText.IsChecked = true;
                _chkTex.IsChecked = true;
                _txtQuery.Text = Program.AutoSearch;
                await Task.Delay(600);

                BtnSearch_Click(null, null);
                await Task.Delay(9000);

                if (_view.Count > 0)
                    _grid.SelectedIndex = 0;

                await Task.Delay(4000);
                File.WriteAllText(dump, _txtLog.Text ?? "");
            }
            catch (Exception ex)
            {
                Log("ERROR autotest: " + ex);
            }
        }

        // ---- zoom / arrastre de la previsualización de imagen ----
        private volatile bool _searchRunning;
        private double _imgZoom;            // 0 = ajustado al panel
        private bool _panning;
        private Point _panOrigen;
        private Vector _panOffset;

        private void WireImageZoom()
        {
            if (_scrollImage == null || _imgPreview == null)
                return;

            _scrollImage.PointerWheelChanged += (s, e) =>
            {
                if (_imgPreview.Source == null)
                    return;

                double baseZ = _imgZoom > 0 ? _imgZoom : EscalaAjuste();
                double destino = e.Delta.Y > 0 ? baseZ * 1.25 : baseZ / 1.25;
                ZoomEn(destino, e.GetPosition(_scrollImage));
                e.Handled = true;
            };

            var panel = _imageHost ?? (Control)_imgPreview;
            panel.PointerPressed += (s, e) =>
            {
                var p = e.GetCurrentPoint(_imgPreview);
                if (!p.Properties.IsLeftButtonPressed)
                    return;

                if (e.ClickCount == 2)
                {
                    FitImage();
                    e.Handled = true;
                    return;
                }

                _panning = true;
                _panOrigen = e.GetPosition(_scrollImage);
                _panOffset = _scrollImage.Offset;
                e.Pointer.Capture(panel);
                e.Handled = true;
            };

            panel.PointerMoved += (s, e) =>
            {
                if (!_panning)
                    return;
                var p = e.GetPosition(_scrollImage);
                double x = _panOffset.X - (p.X - _panOrigen.X);
                double y = _panOffset.Y - (p.Y - _panOrigen.Y);
                _scrollImage.Offset = new Vector(Math.Max(0, x), Math.Max(0, y));
                e.Handled = true;
            };

            panel.PointerReleased += (s, e) =>
            {
                if (!_panning)
                    return;
                _panning = false;
                e.Pointer.Capture(null);
                e.Handled = true;
            };
        }

        private double EscalaAjuste()
        {
            var px = (_imgPreview?.Source as Bitmap)?.PixelSize;
            if (px == null || px.Value.Width <= 0 || px.Value.Height <= 0)
                return 1;

            var vp = _scrollImage.Viewport;
            double vw = Math.Max(64, (vp.Width > 0 ? vp.Width : _scrollImage.Bounds.Width) - 24);
            double vh = Math.Max(64, (vp.Height > 0 ? vp.Height : _scrollImage.Bounds.Height) - 24);
            double s = Math.Min(vw / px.Value.Width, vh / px.Value.Height);
            // las imagenes pequenas se amplian para llenar el panel (hasta 8x)
            return s <= 0 ? 1 : Math.Min(s, 8.0);
        }

        /// <summary>Ajusta la imagen al panel (doble clic o al abrir otra textura).</summary>
        private void FitImage()
        {
            if (_imgPreview?.Source is not Bitmap)
                return;

            _imgZoom = EscalaAjuste();
            ApplyZoom();
            Dispatcher.UIThread.Post(() => _scrollImage.Offset = new Vector(0, 0), DispatcherPriority.Loaded);
        }

        /// <summary>El panel contenedor nunca es menor que el viewport: así la imagen queda centrada.</summary>
        private void AjustarHost(double ancho, double alto)
        {
            if (_imageHost == null)
                return;

            var vp = _scrollImage?.Viewport ?? default;
            double vw = vp.Width > 0 ? vp.Width : (_scrollImage?.Bounds.Width ?? 0);
            double vh = vp.Height > 0 ? vp.Height : (_scrollImage?.Bounds.Height ?? 0);

            _imageHost.Width = Math.Max(ancho, vw);
            _imageHost.Height = Math.Max(alto, vh);
        }

        private double MargenX()
        {
            if (_imageHost == null || _imgPreview == null)
                return 0;
            double w = _imgPreview.Width;
            if (double.IsNaN(w))
                return 0;
            return Math.Max(0, (_imageHost.Width - w) / 2);
        }

        private double MargenY()
        {
            if (_imageHost == null || _imgPreview == null)
                return 0;
            double h = _imgPreview.Height;
            if (double.IsNaN(h))
                return 0;
            return Math.Max(0, (_imageHost.Height - h) / 2);
        }

        /// <summary>Zoom anclado al puntero: el píxel que hay bajo el cursor no se mueve.</summary>
        private void ZoomEn(double destino, Point ancla)
        {
            if (_imgPreview?.Source is not Bitmap bmp)
                return;

            var px = bmp.PixelSize;
            if (px.Width <= 0 || px.Height <= 0)
                return;

            double viejo = _imgZoom > 0 ? _imgZoom : EscalaAjuste();
            if (viejo <= 0)
                viejo = 1;

            // punto de la imagen (en píxeles) bajo el cursor, descontando el centrado del host
            double ix = (_scrollImage.Offset.X + ancla.X - MargenX()) / viejo;
            double iy = (_scrollImage.Offset.Y + ancla.Y - MargenY()) / viejo;

            _imgZoom = Math.Clamp(destino, 0.02, 64.0);
            ApplyZoom();

            double nx = MargenX() + ix * _imgZoom - ancla.X;
            double ny = MargenY() + iy * _imgZoom - ancla.Y;

            // el nuevo tamaño se aplica en el siguiente ciclo de layout: mover el scroll después
            Dispatcher.UIThread.Post(
                () => _scrollImage.Offset = new Vector(Math.Max(0, nx), Math.Max(0, ny)),
                DispatcherPriority.Loaded);
        }

        private void ApplyZoom()
        {
            if (_imgPreview?.Source is not Bitmap bmp)
                return;

            var px = bmp.PixelSize;
            double w = Math.Max(1, px.Width * _imgZoom);
            double h = Math.Max(1, px.Height * _imgZoom);

            _imgPreview.Stretch = Stretch.Fill;
            _imgPreview.Width = w;
            _imgPreview.Height = h;
            _imgPreview.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            _imgPreview.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            _imgPreview.Cursor = new Cursor(StandardCursorType.Hand);

            AjustarHost(w, h);
            AnotarZoom();
        }

        /// <summary>Muestra/actualiza el porcentaje de zoom en la línea de información.</summary>
        private void AnotarZoom()
        {
            if (_txtPreviewInfo == null)
                return;

            string txt = System.Text.RegularExpressions.Regex.Replace(_txtPreviewInfo.Text ?? "", @"\s+·\s+\d+\s?%", "");
            _txtPreviewInfo.Text = _imgZoom > 0 ? txt + "   ·   " + Math.Round(_imgZoom * 100) + "%" : txt;
        }

        private sealed class LayoutState
        {
            public double PreviewWidth { get; set; } = 520;
            public Dictionary<string, double> Columns { get; set; } = new Dictionary<string, double>();
        }

        private static string LayoutPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZeroUnityBatch", "layout.json");

        private void SaveLayout()
        {
            try
            {
                var st = new LayoutState();

                if (_gridBody != null && _gridBody.ColumnDefinitions.Count > 2)
                    st.PreviewWidth = _gridBody.ColumnDefinitions[2].Width.Value;

                foreach (var col in _grid.Columns)
                {
                    string key = col.Header?.ToString() ?? "";
                    if (key.Length > 0 && col.ActualWidth > 0)
                        st.Columns[key] = Math.Round(col.ActualWidth);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath));
                File.WriteAllText(LayoutPath, System.Text.Json.JsonSerializer.Serialize(st));
            }
            catch { }
        }

        private void RestoreLayout()
        {
            try
            {
                if (!File.Exists(LayoutPath))
                    return;

                var st = System.Text.Json.JsonSerializer.Deserialize<LayoutState>(File.ReadAllText(LayoutPath));
                if (st == null)
                    return;

                if (st.PreviewWidth > 240 && _gridBody != null && _gridBody.ColumnDefinitions.Count > 2)
                    _gridBody.ColumnDefinitions[2].Width = new GridLength(st.PreviewWidth, GridUnitType.Pixel);

                foreach (var col in _grid.Columns)
                {
                    string key = col.Header?.ToString() ?? "";
                    if (st.Columns.TryGetValue(key, out double ancho) && ancho > 24)
                        col.Width = new DataGridLength(ancho, DataGridLengthUnitType.Pixel);
                }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveLayout();
            base.OnClosed(e);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            RestoreLayout();

            var screen = Screens.ScreenFromWindow(this);
            if (screen != null && WindowState != WindowState.Maximized)
            {
                double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
                double maxW = screen.WorkingArea.Width / scale - 40;
                double maxH = screen.WorkingArea.Height / scale - 40;
                if (Width > maxW) Width = maxW;
                if (Height > maxH) Height = maxH;
                Position = new PixelPoint(
                    screen.WorkingArea.X + (int)((screen.WorkingArea.Width - Width * scale) / 2),
                    screen.WorkingArea.Y + (int)((screen.WorkingArea.Height - Height * scale) / 2));
            }

            if (!string.IsNullOrEmpty(Program.StartupPath))
            {
                string p = Program.StartupPath;
                Program.StartupPath = null;
                _ = OpenAndMaybeSearchAsync(p);
            }
        }
    }
}
