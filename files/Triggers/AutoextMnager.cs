/*/ nuget -\YamlDotNet; /*/
using Au.Triggers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using YamlDotNet.Serialization;
using System.Threading;
using System.Drawing;

partial class Program {
	
	[System.Runtime.InteropServices.DllImport("user32.dll")]
	static extern bool SetForegroundWindow(IntPtr hWnd);
	
	[System.Runtime.InteropServices.DllImport("user32.dll")]
	static extern bool BringWindowToTop(IntPtr hWnd);
	
	static NotifyIcon _notifyIcon;
	static FileSystemWatcher _watcher;
	static System.Threading.Timer _reloadTimer;
	static readonly object _lock = new object();
	
	// === Структуры данных ===
	class Snippet {
		public string trigger { get; set; }
		public List<string> triggers { get; set; }
		public string form { get; set; }
		public Dictionary<string, FieldOptions> form_fields { get; set; }
		
		public List<string> GetAllTriggers() {
			var all = new List<string>();
			if (!string.IsNullOrEmpty(trigger)) all.Add(trigger);
			if (triggers != null) all.AddRange(triggers);
			return all;
		}
	}
	
	class FieldOptions {
		public bool multiline { get; set; } = false;
		// УДАЛЕНО: public string list { get; set; } = null;
		// → списки теперь задаются в шаблоне: [[name=a|b|c]]
	}
	
	// === Кэш сниппетов ===
	static List<Snippet> _snippets;
	static void ShowQuietNotification(string title, string text) {
		Thread thread = new Thread(() => {
			try {
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				
				var f = new Form {
					FormBorderStyle = FormBorderStyle.None,
					Width = 350,
					Height = 120,
					StartPosition = FormStartPosition.Manual,
					TopMost = true,
					ShowInTaskbar = false,
					BackColor = Color.FromArgb(40, 40, 40),
					Opacity = 0.95,
					ControlBox = false,
					MaximizeBox = false,
					MinimizeBox = false,
					Text = ""
				};
				
				// Закруглённые углы
				using (var path = new System.Drawing.Drawing2D.GraphicsPath()) {
					int r = 12;
					path.AddArc(0, 0, r, r, 180, 90);
					path.AddArc(f.Width - r, 0, r, r, 270, 90);
					path.AddArc(f.Width - r, f.Height - r, r, r, 0, 90);
					path.AddArc(0, f.Height - r, r, r, 90, 90);
					path.CloseFigure();
					f.Region = new Region(path);
				}
				
				var label = new Label {
					Text = $"{title}\n{text}",
					Dock = DockStyle.Fill,
					Font = new Font("Segoe UI", 10, FontStyle.Bold),
					ForeColor = Color.Lime,
					BackColor = Color.FromArgb(40, 40, 40),
					TextAlign = ContentAlignment.MiddleLeft,
					Padding = new Padding(15, 10, 15, 10),
					Cursor = Cursors.Hand
				};
				
				f.Controls.Add(label);
				
				// Позиция
				var screen = Screen.PrimaryScreen.WorkingArea;
				f.Left = screen.Right - f.Width - 20;
				f.Top = screen.Bottom - f.Height - 20;
				
				// Авто-закрытие
				var timer = new System.Windows.Forms.Timer { Interval = 3000 };
				timer.Tick += (s, e) => {
					timer.Dispose();
					f.Close();
				};
				
				f.Load += (s, e) => timer.Start();
				f.Click += (s, e) => f.Close();
				label.Click += (s, e) => f.Close();
				
				Application.Run(f);
			}
			catch {
				// Игнорируем ошибки (например, если экран недоступен)
			}
		});
		
		thread.SetApartmentState(ApartmentState.STA);
		thread.IsBackground = true;
		thread.Start();
	}
	
	static string ResolveSpecialVariables(string value) {
		if (string.IsNullOrEmpty(value)) return value;
		
		if (value == "{CLIPBOARD}") {
			try {
				if (Clipboard.ContainsText()) {
					return Clipboard.GetText().Trim();
				}
			}
			catch {
				// Игнорируем
			}
			return "";
		}
		
		if (value == "{DATE}") {
			return DateTime.Now.ToString("yyyy-MM-dd");
		}
		
		if (value == "{USER}") {
			return Environment.UserName;
		}
		
		if (value == "{NOW}") {
			return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		}
		
		return value;
	}
	
	
	
	// === Загрузка сниппетов ===
	static void LoadYamlSnippets(bool forceReload = false) {
		lock (_lock) {
			if (_snippets != null && !forceReload) return;
			
			try {
				var path = @"I:\snippets-libreautomate\snippets.yaml";
				if (!File.Exists(path))
					throw new FileNotFoundException("snippets.yaml not found", path);
				
				string yaml;
				using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
					using (var reader = new StreamReader(fs)) {
						yaml = reader.ReadToEnd();
					}
				}
				
				var deserializer = new DeserializerBuilder().Build();
				var list = deserializer.Deserialize<List<Snippet>>(yaml);
				_snippets = list;
				
				// Показываем уведомление, если это перезагрузка
				if (forceReload && _notifyIcon != null) {
					//_notifyIcon.ShowBalloonTip(2000, "Snippets обновлены", "Изменения загружены автоматически.", ToolTipIcon.Info);
					ShowQuietNotification("Snippets обновлены", "Изменения загружены автоматически.");
				}
			}
			catch (Exception ex) {
				MessageBox.Show($"YAML Autotext error:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				_snippets = null; // сбрасываем кэш при ошибке
			}
		}
	}
	
	
	// === Регистрация триггеров ===
	[Triggers]
	void YamlAutotextTriggers() {
		if (_notifyIcon == null) {
			InitializeTrayAndWatcher();
		}
		
		LoadYamlSnippets(); // загружаем при первом вызове
		if (_snippets == null) return;
		
		var tt = Triggers.Autotext;
		
		// Собираем все триггеры, которых ещё нет в tt
		var knownTriggers = new HashSet<string>();
		foreach (var snippet in _snippets) {
			if (snippet == null) continue;
			foreach (var trig in snippet.GetAllTriggers()) {
				if (string.IsNullOrEmpty(trig)) continue;
				// Регистрируем ТОЛЬКО если ещё не зарегистрирован
				if (!knownTriggers.Contains(trig)) {
					knownTriggers.Add(trig);
					var trigger = trig;
					tt[trigger] = o => {
						// Ищем сниппет по trigger в АКТУАЛЬНОМ _snippets
						Snippet currentSnippet = null;
						lock (_lock) {
							if (_snippets != null) {
								foreach (var s in _snippets) {
									if (s.GetAllTriggers().Contains(trigger)) {
										currentSnippet = s;
										break;
									}
								}
							}
						}
						if (currentSnippet == null) return;
						
						o.Replace("");
						var result = ProcessSnippet(currentSnippet);
						if (!string.IsNullOrEmpty(result)) {
							keys.sendt(result);
						}
					};
				}
			}
		}
	}
	
	// === Обработка формы ===
	static string ProcessSnippet(Snippet snippet) {
		var formLines = snippet.form.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
		
		var form = new Form {
			Text = "Input",
			Width = 1200,
			Height = 280,
			StartPosition = FormStartPosition.CenterScreen,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			MinimizeBox = false,
			MaximizeBox = false,
			TopMost = true,
			ShowInTaskbar = false
		};
		
		form.Shown += (s, e) => {
			var f = (Form)s;
			f.TopMost = true;
			BringWindowToTop(f.Handle);
			SetForegroundWindow(f.Handle);
		};
		
		var flow = new FlowLayoutPanel {
			Dock = DockStyle.Fill,
			AutoScroll = true,
			Padding = new Padding(15),
			FlowDirection = FlowDirection.TopDown,
			AutoSize = false
		};
		
		var inputs = new Dictionary<string, Tuple<Control, string>>();
		
		// Сначала найдём все объявления $name:value$
		var declaredVars = new Dictionary<string, string>();
		foreach (var line in formLines) {
			var declMatches = Regex.Matches(line, @"\$(\w+):([^$]+)\$");
			foreach (Match dm in declMatches) {
				string name = dm.Groups[1].Value;
				string value = dm.Groups[2].Value;
				declaredVars[name] = ResolveSpecialVariables(value);
			}
		}
		
		// Теперь строим форму: обрабатываем ТОЛЬКО [[...]]
		foreach (var line in formLines) {
			if (string.IsNullOrWhiteSpace(line)) {
				flow.Controls.Add(new Panel { Height = 6 });
				continue;
			}
			
			var linePanel = new FlowLayoutPanel {
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				AutoSize = true
			};
			
			var matches = Regex.Matches(line, @"\[\[([^\]]+)\]\]");
			int lastEnd = 0;
			
			for (int i = 0; i < matches.Count; i++) {
				var match = matches[i];
				string before = line.Substring(lastEnd, match.Index - lastEnd);
				if (!string.IsNullOrEmpty(before)) {
					linePanel.Controls.Add(new Label {
						Text = before,
						AutoSize = true,
						Font = new System.Drawing.Font("Consolas", 9)
					});
				}
				
				string spec = match.Groups[1].Value.Trim();
				string name, defaultValue;
				if (spec.Contains("=")) {
					int eq = spec.IndexOf('=');
					name = spec.Substring(0, eq).Trim();
					defaultValue = spec.Substring(eq + 1).Trim();
					// Разрешаем специальные переменные
					defaultValue = ResolveSpecialVariables(defaultValue);
				} else {
					name = spec;
					defaultValue = ResolveSpecialVariables(name);
				}
				
				Control inputControl = null;
				string finalDefault = defaultValue;
				
				// === ИЗМЕНЕНО: встроенная поддержка списков через |
				string[] inlineItems = null;
				char separator = '\0';
				
				// Проверяем разделители в порядке приоритета: , ; /
				if (defaultValue.Contains(',')) {
					separator = ',';
				} else if (defaultValue.Contains(';')) {
					separator = ';';
				} else if (defaultValue.Contains('/')) {
					separator = '/';
				} else if (defaultValue.Contains('|')) {
					separator = '|';
				}
				
				if (separator != '\0') {
					inlineItems = defaultValue.Split(separator);
					// Разрешаем специальные переменные в каждом элементе
					for (int j = 0; j < inlineItems.Length; j++) {
						inlineItems[j] = ResolveSpecialVariables(inlineItems[j]);
					}
					if (inlineItems.Length > 0) {
						finalDefault = inlineItems[0];
					}
				}
				
				if (inlineItems != null && inlineItems.Length > 1) {
					// === ДОБАВЛЕНО: редактируемый ComboBox
					var combo = new ComboBox {
						Width = Math.Max(100, finalDefault.Length * 8 + 20),
						DropDownStyle = ComboBoxStyle.DropDown, // ← editable!
						Font = new System.Drawing.Font("Consolas", 9)
					};
					combo.Items.AddRange(inlineItems);
					combo.Text = finalDefault; // ← устанавливаем текст, не выбор
					inputControl = combo;
				} else {
					// Обычное поле (с поддержкой multiline)
					FieldOptions opts = null;
					bool isMultiline = snippet.form_fields?.TryGetValue(name, out opts) == true && opts.multiline;
					
					var tb = new TextBox {
						Width = isMultiline ? 500 : 100,
						Height = isMultiline ? 60 : 25,
						Multiline = isMultiline,
						ScrollBars = isMultiline ? ScrollBars.Vertical : ScrollBars.None,
						Font = new System.Drawing.Font("Consolas", 9),
						PlaceholderText = defaultValue,
						Text = ""
					};
					
					if (isMultiline) {
						inputControl = tb;
					} else {
						var wrapper = new Panel {
							Width = 100,
							Height = 25,
							Margin = new Padding(0, -2, 0, 0)
						};
						wrapper.Controls.Add(tb);
						tb.Dock = DockStyle.Fill;
						inputControl = wrapper;
					}
				}
				
				inputs[name] = Tuple.Create(inputControl, finalDefault);
				linePanel.Controls.Add(inputControl);
				lastEnd = match.Index + match.Length;
			}
			
			if (lastEnd < line.Length) {
				string after = line.Substring(lastEnd);
				if (!string.IsNullOrEmpty(after)) {
					linePanel.Controls.Add(new Label {
						Text = after,
						AutoSize = true,
						Font = new System.Drawing.Font("Consolas", 9)
					});
				}
			}
			
			flow.Controls.Add(linePanel);
		}
		
		// Кнопка OK
		var bottomPanel = new Panel {
			Height = 40,
			Dock = DockStyle.Bottom,
			Padding = new Padding(0, 10, 15, 10)
		};
		var btn = new Button {
			Text = "OK",
			Width = 80,
			Height = 26,
			DialogResult = DialogResult.OK,
			Anchor = AnchorStyles.Top | AnchorStyles.Right
		};
		bottomPanel.Controls.Add(btn);
		
		form.Controls.Add(bottomPanel);
		form.Controls.Add(flow);
		
		if (form.ShowDialog() != DialogResult.OK)
			return null;
		
		// Собираем значения полей
		var fieldValues = new Dictionary<string, string>();
		foreach (var kvp in inputs) {
			string name = kvp.Key;
			var (control, def) = kvp.Value;
			string val = def;
			
			if (control is Panel p && p.Controls[0] is TextBox tb) {
				val = string.IsNullOrWhiteSpace(tb.Text) ? def : tb.Text;
			} else if (control is ComboBox cb) {
				// === ИСПРАВЛЕНО: используем .Text, а не SelectedItem
				val = string.IsNullOrWhiteSpace(cb.Text) ? def : cb.Text;
			}
			
			fieldValues[name] = val;
		}
		
		// Обновляем объявленные переменные
		foreach (var name in declaredVars.Keys.ToList()) {
			if (fieldValues.TryGetValue(name, out string uiValue)) {
				declaredVars[name] = uiValue;
			}
		}
		
		// Сборка результата
		var resultLines = new List<string>();
		foreach (var line in formLines) {
			string outLine = line;
			
			// 1. Заменяем объявления: $var:i$ → i
			foreach (var kvp in declaredVars) {
				string name = kvp.Key;
				string val = kvp.Value;
				string pattern = @"\$" + Regex.Escape(name) + @":[^$]+\$";
				outLine = Regex.Replace(outLine, pattern, val);
			}
			
			// 2. Заменяем подстановки: $var$ → i
			foreach (var kvp in declaredVars) {
				string name = kvp.Key;
				string val = kvp.Value;
				outLine = Regex.Replace(outLine, @"\$" + Regex.Escape(name) + @"\$", val);
			}
			
			// 3. Заменяем поля: [[...]]
			foreach (var kvp in inputs) {
				string name = kvp.Key;
				string val = fieldValues[name];
				string pattern = $@"\[\[\s*{Regex.Escape(name)}\s*(?:=[^]]*)?\]\]";
				outLine = Regex.Replace(outLine, pattern, val);
			}
			
			resultLines.Add(outLine);
		}
		
		return string.Join("\r\n", resultLines);
	}
	
	static void InitializeTrayAndWatcher() {
		if (_notifyIcon != null) return;
		
		_notifyIcon = new NotifyIcon {
			Text = "YAML Autotext",
			Visible = true,
			Icon = SystemIcons.Information
		};
		
		var contextMenu = new ContextMenuStrip();
		var reloadItem = new ToolStripMenuItem("Перезагрузить сниппеты");
		reloadItem.Click += (s, e) => {
			_snippets = null;
			LoadYamlSnippets(forceReload: true);
		};
		contextMenu.Items.Add(reloadItem);
		
		var exitItem = new ToolStripMenuItem("Выход");
		exitItem.Click += (s, e) => {
			_notifyIcon.Visible = false;
			_notifyIcon.Dispose();
			Environment.Exit(0);
		};
		contextMenu.Items.Add(exitItem);
		
		_notifyIcon.ContextMenuStrip = contextMenu; // ← ЭТА СТРОКА ОБЯЗАТЕЛЬНА!
		
		_watcher = new FileSystemWatcher {
			Path = @"I:\snippets-libreautomate",
			Filter = "snippets.yaml",
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
		};
		_watcher.Changed += (s, e) => {
			// Отменяем предыдущий таймер (если есть)
			_reloadTimer?.Dispose();
			
			// Запускаем новый таймер — перезагрузка через 500 мс
			_reloadTimer = new System.Threading.Timer(_ => {
				lock (_lock) {
					_snippets = null;
					LoadYamlSnippets(forceReload: true);
				}
			}, null, 500, Timeout.Infinite);
		};
		_watcher.EnableRaisingEvents = true;
	}
}