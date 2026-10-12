using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using System.Text.Json;

namespace Artry.Mobile;

[Activity(
    Label = "@string/app_name",
    MainLauncher = true,
    Theme = "@style/Theme.Artry.Dark",
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation | Android.Content.PM.ConfigChanges.ScreenSize)]
public class MainActivity : Activity
{
    private const int RequestCodeOpenFile = 1001;
    private const int RequestCodeSaveFile = 1002;
    private const string PrefsName = "artry_prefs";
    private const string KeyRecents = "recent_documents";

    // Containers
    private LinearLayout _layoutHome = null!;
    private LinearLayout _layoutEditor = null!;
    private LinearLayout _containerHomeCards = null!;

    // Editor Views
    private EditText _editor = null!;
    private TextView _lblEditorTitle = null!;
    private TextView _lblDocStatus = null!;
    private TextView _lblWordStats = null!;
    private Button _btnBackToHome = null!;

    private Android.Net.Uri? _currentFileUri;
    private string _currentDocName = "Sem título";
    private float _currentEditorSizeSp = 16f;

    public class RecentDocItem
    {
        public string Title { get; set; } = "";
        public string? UriString { get; set; }
        public bool IsNew { get; set; }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        // Bind Containers
        _layoutHome = FindViewById<LinearLayout>(Resource.Id.layoutHome)!;
        _layoutEditor = FindViewById<LinearLayout>(Resource.Id.layoutEditor)!;
        _containerHomeCards = FindViewById<LinearLayout>(Resource.Id.containerHomeCards)!;

        // Home Top Bar Buttons
        var btnHomeOpen = FindViewById<Button>(Resource.Id.btnHomeOpen)!;
        var btnHomeNew = FindViewById<Button>(Resource.Id.btnHomeNew)!;
        btnHomeOpen.Click += (_, _) => OnOpenDocument();
        btnHomeNew.Click += (_, _) => OpenNewDocumentEditor();

        // Editor Views
        _editor = FindViewById<EditText>(Resource.Id.txtEditor)!;
        _lblEditorTitle = FindViewById<TextView>(Resource.Id.lblEditorTitle)!;
        _lblDocStatus = FindViewById<TextView>(Resource.Id.lblDocStatus)!;
        _lblWordStats = FindViewById<TextView>(Resource.Id.lblWordStats)!;
        _btnBackToHome = FindViewById<Button>(Resource.Id.btnBackToHome)!;

        _btnBackToHome.Click += (_, _) => ShowHome();

        // Editor Action Buttons
        var btnNew = FindViewById<Button>(Resource.Id.btnNew)!;
        var btnOpen = FindViewById<Button>(Resource.Id.btnOpen)!;
        var btnSave = FindViewById<Button>(Resource.Id.btnSave)!;
        var btnShare = FindViewById<Button>(Resource.Id.btnShare)!;

        btnNew.Click += (_, _) => OnNewDocument();
        btnOpen.Click += (_, _) => OnOpenDocument();
        btnSave.Click += (_, _) => OnSaveDocument();
        btnShare.Click += (_, _) => OnShareDocument();

        // Formatting Buttons
        var btnBold = FindViewById<Button>(Resource.Id.btnFmtBold)!;
        var btnItalic = FindViewById<Button>(Resource.Id.btnFmtItalic)!;
        var btnUnderline = FindViewById<Button>(Resource.Id.btnFmtUnderline)!;
        var btnStrike = FindViewById<Button>(Resource.Id.btnFmtStrike)!;
        var btnH1 = FindViewById<Button>(Resource.Id.btnFmtH1)!;
        var btnH2 = FindViewById<Button>(Resource.Id.btnFmtH2)!;
        var btnBullet = FindViewById<Button>(Resource.Id.btnFmtBullet)!;
        var btnNumbered = FindViewById<Button>(Resource.Id.btnFmtNumbered)!;
        var btnSizeMinus = FindViewById<Button>(Resource.Id.btnSizeMinus)!;
        var btnSizePlus = FindViewById<Button>(Resource.Id.btnSizePlus)!;
        var btnClear = FindViewById<Button>(Resource.Id.btnClearFormat)!;

        btnBold.Click += (_, _) => ApplyStyleSpan(new StyleSpan(TypefaceStyle.Bold));
        btnItalic.Click += (_, _) => ApplyStyleSpan(new StyleSpan(TypefaceStyle.Italic));
        btnUnderline.Click += (_, _) => ApplySpan(new UnderlineSpan());
        btnStrike.Click += (_, _) => ApplySpan(new StrikethroughSpan());
        btnH1.Click += (_, _) => ApplySpan(new RelativeSizeSpan(1.45f));
        btnH2.Click += (_, _) => ApplySpan(new RelativeSizeSpan(1.22f));
        btnBullet.Click += (_, _) => InsertLinePrefix("• ");
        btnNumbered.Click += (_, _) => InsertLinePrefix("1. ");

        btnSizeMinus.Click += (_, _) =>
        {
            if (_currentEditorSizeSp > 12f)
            {
                _currentEditorSizeSp -= 2f;
                _editor.SetTextSize(Android.Util.ComplexUnitType.Sp, _currentEditorSizeSp);
            }
        };

        btnSizePlus.Click += (_, _) =>
        {
            if (_currentEditorSizeSp < 32f)
            {
                _currentEditorSizeSp += 2f;
                _editor.SetTextSize(Android.Util.ComplexUnitType.Sp, _currentEditorSizeSp);
            }
        };

        btnClear.Click += (_, _) => ClearSelectionFormatting();

        _editor.TextChanged += (_, _) => UpdateWordStats();

        // Populate Home Screen cards
        PopulateHomeCards();

        // Handle incoming intent if opened via "Open with"
        HandleIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }

#pragma warning disable CA1422
    public override void OnBackPressed()
    {
        if (_layoutEditor.Visibility == ViewStates.Visible)
        {
            ShowHome();
            return;
        }
        base.OnBackPressed();
    }
#pragma warning restore CA1422

    private void ShowHome()
    {
        HideKeyboard();
        _layoutEditor.Visibility = ViewStates.Gone;
        _layoutHome.Visibility = ViewStates.Visible;
        PopulateHomeCards();
    }

    private void ShowEditor()
    {
        _layoutHome.Visibility = ViewStates.Gone;
        _layoutEditor.Visibility = ViewStates.Visible;
        UpdateDocStatus();
        UpdateWordStats();
    }

    private void HideKeyboard()
    {
        try
        {
            var imm = (InputMethodManager?)GetSystemService(InputMethodService);
            if (imm != null && CurrentFocus != null)
            {
                imm.HideSoftInputFromWindow(CurrentFocus.WindowToken, 0);
            }
        }
        catch { }
    }

    private void OpenNewDocumentEditor(string? sampleTitle = null)
    {
        _editor.Text = "";
        _currentFileUri = null;
        _currentDocName = sampleTitle ?? "Sem título";
        ShowEditor();
    }

    private void PopulateHomeCards()
    {
        _containerHomeCards.RemoveAllViews();

        var cardList = new List<RecentDocItem>
        {
            // First card is always "Novo documento"
            new RecentDocItem { Title = "Novo documento", UriString = null, IsNew = true }
        };

        var savedRecents = LoadRecentDocs();
        if (savedRecents.Count > 0)
        {
            cardList.AddRange(savedRecents);
        }

        // Layout into 2-column grid rows
        var density = Resources?.DisplayMetrics?.Density ?? 1.5f;
        int rowMarginBottom = (int)(14 * density);
        int cardMargin = (int)(6 * density);

        for (int i = 0; i < cardList.Count; i += 2)
        {
            var row = new LinearLayout(this)
            {
                Orientation = Orientation.Horizontal,
                LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.WrapContent)
                {
                    BottomMargin = rowMarginBottom
                }
            };

            // First card in row
            var cardView1 = CreateCardView(cardList[i], cardMargin);
            row.AddView(cardView1);

            // Second card in row (or spacer if odd count)
            if (i + 1 < cardList.Count)
            {
                var cardView2 = CreateCardView(cardList[i + 1], cardMargin);
                row.AddView(cardView2);
            }
            else
            {
                // Empty spacer to maintain 50% width
                var spacer = new View(this)
                {
                    LayoutParameters = new LinearLayout.LayoutParams(
                        0,
                        ViewGroup.LayoutParams.MatchParent,
                        1f)
                    {
                        LeftMargin = cardMargin,
                        RightMargin = cardMargin
                    }
                };
                row.AddView(spacer);
            }

            _containerHomeCards.AddView(row);
        }
    }

    private View CreateCardView(RecentDocItem item, int margin)
    {
        var card = LayoutInflater.Inflate(Resource.Layout.card_document_item, null)!;
        var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            LeftMargin = margin,
            RightMargin = margin
        };
        card.LayoutParameters = lp;

        var imgThumb = card.FindViewById<ImageView>(Resource.Id.imgCardThumbnail)!;
        var lblTitle = card.FindViewById<TextView>(Resource.Id.lblCardTitle)!;

        lblTitle.Text = item.Title.ToUpperInvariant();

        // Draw custom wavy text lines or plus thumbnail
        var bmp = GenerateCardThumbnail(280, 240, item.IsNew);
        imgThumb.SetImageBitmap(bmp);

        card.Click += (_, _) =>
        {
            if (item.IsNew)
            {
                OpenNewDocumentEditor();
            }
            else if (!string.IsNullOrEmpty(item.UriString))
            {
                var uri = Android.Net.Uri.Parse(item.UriString);
                if (uri != null)
                {
                    LoadFromUri(uri);
                    ShowEditor();
                }
                else
                {
                    OpenNewDocumentEditor(item.Title);
                }
            }
            else
            {
                OpenNewDocumentEditor(item.Title);
            }
        };

        if (!item.IsNew && !string.IsNullOrEmpty(item.UriString))
        {
            card.LongClick += (_, _) =>
            {
                var builder = new AlertDialog.Builder(this);
                builder.SetTitle(item.Title);
                builder.SetItems(new[] { "Abrir documento", "Remover dos recentes" }, (dialog, which) =>
                {
                    if (which.Which == 0)
                    {
                        var uri = Android.Net.Uri.Parse(item.UriString);
                        if (uri != null) { LoadFromUri(uri); ShowEditor(); }
                    }
                    else if (which.Which == 1)
                    {
                        RemoveRecentDoc(item.UriString);
                        PopulateHomeCards();
                    }
                });
                builder.Show();
            };
        }

        return card;
    }

    private static Bitmap GenerateCardThumbnail(int w, int h, bool isNewDoc)
    {
        var bitmap = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(bitmap);

        // Folha de papel branca/creme
        canvas.DrawColor(Color.Rgb(252, 252, 254));

        using var borderPaint = new Paint
        {
            AntiAlias = true,
            Color = Color.Rgb(215, 220, 228),
            StrokeWidth = 2f
        };
        borderPaint.SetStyle(Paint.Style.Stroke);
        canvas.DrawRect(2, 2, w - 2, h - 2, borderPaint);

        // Canto superior direito dobrado (dog-ear)
        int fold = (int)(w * 0.12f);
        using var foldPath = new Android.Graphics.Path();
        foldPath.MoveTo(w - fold, 0);
        foldPath.LineTo(w, fold);
        foldPath.LineTo(w - fold, fold);
        foldPath.Close();

        using var foldPaint = new Paint
        {
            AntiAlias = true,
            Color = Color.Rgb(230, 234, 240)
        };
        foldPaint.SetStyle(Paint.Style.Fill);
        canvas.DrawPath(foldPath, foldPaint);

        using var foldLinePaint = new Paint
        {
            AntiAlias = true,
            Color = Color.Rgb(195, 202, 212),
            StrokeWidth = 2f
        };
        foldLinePaint.SetStyle(Paint.Style.Stroke);
        canvas.DrawLine(w - fold, 0, w - fold, fold, foldLinePaint);
        canvas.DrawLine(w - fold, fold, w, fold, foldLinePaint);

        if (isNewDoc)
        {
            // Ícone limpo de '+' no centro
            using var paintPlus = new Paint
            {
                AntiAlias = true,
                StrokeWidth = 6.5f,
                Color = Color.Rgb(115, 125, 140)
            };
            paintPlus.SetStyle(Paint.Style.Stroke);
            paintPlus.StrokeCap = Paint.Cap.Round;

            int cx = w / 2;
            int cy = h / 2;
            int r = (int)(w * 0.14f);
            canvas.DrawLine(cx - r, cy, cx + r, cy, paintPlus);
            canvas.DrawLine(cx, cy - r, cx, cy + r, paintPlus);
        }
        else
        {
            // Linhas de pré-visualização de texto do documento
            using var textLinePaint = new Paint
            {
                AntiAlias = true,
                StrokeWidth = 4.5f,
                Color = Color.Rgb(55, 60, 72)
            };
            textLinePaint.SetStyle(Paint.Style.Stroke);
            textLinePaint.StrokeCap = Paint.Cap.Round;

            int startY = (int)(h * 0.22f);
            int spacing = (int)(h * 0.12f);
            int startX = (int)(w * 0.12f);

            for (int i = 0; i < 6; i++)
            {
                int y = startY + (i * spacing);
                if (y < h - 20)
                {
                    float ratio = i switch
                    {
                        0 => 0.55f,
                        1 => 0.76f,
                        2 => 0.70f,
                        3 => 0.62f,
                        4 => 0.74f,
                        _ => 0.45f
                    };
                    int lineW = (int)(w * ratio);
                    canvas.DrawLine(startX, y, startX + lineW, y, textLinePaint);
                }
            }

            // Película translúcida suave ("meio ofuscada")
            using var frostPaint = new Paint();
            frostPaint.Color = Color.Argb(90, 250, 250, 254);
            frostPaint.SetStyle(Paint.Style.Fill);
            canvas.DrawRect(0, 0, w, h, frostPaint);
        }

        return bitmap;
    }

    private List<RecentDocItem> LoadRecentDocs()
    {
        try
        {
            var prefs = GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var json = prefs?.GetString(KeyRecents, null);
            if (!string.IsNullOrEmpty(json))
            {
                var list = JsonSerializer.Deserialize<List<RecentDocItem>>(json);
                if (list != null) return list;
            }
        }
        catch { }
        return new List<RecentDocItem>();
    }

    private void AddRecentDoc(string title, string? uriString)
    {
        if (string.IsNullOrEmpty(uriString)) return;
        try
        {
            var list = LoadRecentDocs();
            list.RemoveAll(x => string.Equals(x.UriString, uriString, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new RecentDocItem { Title = title, UriString = uriString, IsNew = false });
            if (list.Count > 10) list = list.Take(10).ToList();

            var prefs = GetSharedPreferences(PrefsName, FileCreationMode.Private);
            using var editor = prefs?.Edit();
            editor?.PutString(KeyRecents, JsonSerializer.Serialize(list));
            editor?.Apply();
        }
        catch { }
    }

    private void RemoveRecentDoc(string? uriString)
    {
        if (string.IsNullOrEmpty(uriString)) return;
        try
        {
            var list = LoadRecentDocs();
            list.RemoveAll(x => string.Equals(x.UriString, uriString, StringComparison.OrdinalIgnoreCase));
            var prefs = GetSharedPreferences(PrefsName, FileCreationMode.Private);
            using var editor = prefs?.Edit();
            editor?.PutString(KeyRecents, JsonSerializer.Serialize(list));
            editor?.Apply();
        }
        catch { }
    }

    private void HandleIntent(Intent? intent)
    {
        if (intent == null) return;
        if (intent.Action == Intent.ActionView && intent.Data != null)
        {
            LoadFromUri(intent.Data);
            ShowEditor();
        }
    }

    private void OnNewDocument()
    {
        if (!string.IsNullOrEmpty(_editor.Text))
        {
            var builder = new AlertDialog.Builder(this);
            builder.SetTitle("Novo documento");
            builder.SetMessage("Deseja iniciar um novo documento e limpar o editor?");
            builder.SetPositiveButton("Sim", (_, _) =>
            {
                OpenNewDocumentEditor();
                Toast.MakeText(this, "Novo documento iniciado", ToastLength.Short)?.Show();
            });
            builder.SetNegativeButton("Cancelar", (_, _) => { });
            builder.Show();
        }
        else
        {
            OpenNewDocumentEditor();
        }
    }

    private void OnOpenDocument()
    {
        try
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            string[] mimeTypes = ["text/plain", "text/markdown", "text/rtf", "application/octet-stream"];
            intent.PutExtra(Intent.ExtraMimeTypes, mimeTypes);
            StartActivityForResult(intent, RequestCodeOpenFile);
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, $"Erro ao abrir seletor: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    private void OnSaveDocument()
    {
        if (_currentFileUri != null)
        {
            SaveToUri(_currentFileUri);
            return;
        }

        try
        {
            var intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("text/plain");
            var defaultName = _currentDocName.EndsWith(".artry", StringComparison.OrdinalIgnoreCase) ||
                              _currentDocName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? _currentDocName
                : $"{_currentDocName}.artry";
            intent.PutExtra(Intent.ExtraTitle, defaultName);
            StartActivityForResult(intent, RequestCodeSaveFile);
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, $"Erro ao salvar: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    private void OnShareDocument()
    {
        var text = _editor.Text;
        if (string.IsNullOrEmpty(text))
        {
            Toast.MakeText(this, "O documento está vazio para compartilhar.", ToastLength.Short)?.Show();
            return;
        }

        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraSubject, _currentDocName);
        intent.PutExtra(Intent.ExtraText, text);
        StartActivity(Intent.CreateChooser(intent, "Compartilhar com Artry"));
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (resultCode != Result.Ok || data?.Data == null) return;

        var uri = data.Data;
        if (requestCode == RequestCodeOpenFile)
        {
            LoadFromUri(uri);
            ShowEditor();
        }
        else if (requestCode == RequestCodeSaveFile)
        {
            _currentFileUri = uri;
            SaveToUri(uri);
        }
    }

    private void LoadFromUri(Android.Net.Uri uri)
    {
        try
        {
            using var stream = ContentResolver?.OpenInputStream(uri);
            if (stream == null) return;

            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            _editor.Text = content;
            _currentFileUri = uri;
            _currentDocName = GetFileNameFromUri(uri) ?? "Documento";
            AddRecentDoc(_currentDocName, uri.ToString());
            UpdateDocStatus();
            Toast.MakeText(this, $"Arquivo aberto: {_currentDocName}", ToastLength.Short)?.Show();
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, $"Falha ao abrir arquivo: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    private void SaveToUri(Android.Net.Uri uri)
    {
        try
        {
            using var stream = ContentResolver?.OpenOutputStream(uri);
            if (stream == null) return;

            using var writer = new StreamWriter(stream);
            writer.Write(_editor.Text);
            writer.Flush();

            _currentDocName = GetFileNameFromUri(uri) ?? _currentDocName;
            AddRecentDoc(_currentDocName, uri.ToString());
            UpdateDocStatus();
            Toast.MakeText(this, "Documento salvo com sucesso!", ToastLength.Short)?.Show();
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, $"Falha ao salvar: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    private string? GetFileNameFromUri(Android.Net.Uri uri)
    {
        try
        {
            using var cursor = ContentResolver?.Query(uri, null, null, null, null);
            if (cursor != null && cursor.MoveToFirst())
            {
                var nameIndex = cursor.GetColumnIndex(Android.Provider.IOpenableColumns.DisplayName);
                if (nameIndex >= 0) return cursor.GetString(nameIndex);
            }
        }
        catch { }
        return uri.LastPathSegment;
    }

    private void ApplySpan(Java.Lang.Object span)
    {
        var text = _editor.TextFormatted as ISpannable;
        if (text == null) return;

        var start = _editor.SelectionStart;
        var end = _editor.SelectionEnd;

        if (start == end)
        {
            Toast.MakeText(this, "Selecione um trecho de texto primeiro", ToastLength.Short)?.Show();
            return;
        }

        if (start > end) (start, end) = (end, start);
        text.SetSpan(span, start, end, SpanTypes.ExclusiveExclusive);
    }

    private void ApplyStyleSpan(StyleSpan span)
    {
        ApplySpan(span);
    }

    private void ClearSelectionFormatting()
    {
        var text = _editor.TextFormatted as ISpannable;
        if (text == null) return;

        var start = _editor.SelectionStart;
        var end = _editor.SelectionEnd;
        if (start == end)
        {
            start = 0;
            end = _editor.Length();
        }

        if (start > end) (start, end) = (end, start);

        var existingSpans = text.GetSpans(start, end, Java.Lang.Class.FromType(typeof(CharacterStyle)));
        if (existingSpans != null)
        {
            foreach (var s in existingSpans)
            {
                text.RemoveSpan(s);
            }
        }
        Toast.MakeText(this, "Formatação limpa", ToastLength.Short)?.Show();
    }

    private void InsertLinePrefix(string prefix)
    {
        var start = _editor.SelectionStart;
        var end = _editor.SelectionEnd;
        var text = _editor.Text ?? "";

        if (start > end) (start, end) = (end, start);

        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        _editor.Text = text.Insert(lineStart, prefix);
        _editor.SetSelection(Math.Min(_editor.Length(), end + prefix.Length));
    }

    private void UpdateWordStats()
    {
        var raw = _editor.Text ?? "";
        var chars = raw.Length;
        var words = string.IsNullOrWhiteSpace(raw)
            ? 0
            : raw.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;

        _lblWordStats.Text = $"Palavras: {words}  ·  Caracteres: {chars}";
    }

    private void UpdateDocStatus()
    {
        _lblDocStatus.Text = _currentDocName;
        _lblEditorTitle.Text = _currentDocName;
    }
}
