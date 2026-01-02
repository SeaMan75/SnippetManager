### **YAML-Powered Snippet Manager for LibreAutomate**

This is a **dynamic, file-based snippet expansion tool** built directly inside **LibreAutomate** (a powerful Windows automation engine). It enables developers and power users to create intelligent, interactive text templates with minimal setup and zero dependencies.

#### 🔹 **Key Features**

- **Declarative YAML Configuration**  
  Define snippets in a clean, human-readable `snippets.yaml` file. No compiling, no restarting—just edit and go.

- **Smart Variable System**  
  - Declare variables with `$name:value$` and use them anywhere as `$name$`.  
  - Support for **dynamic defaults**: `[[field={CLIPBOARD}]]`, `[[field={DATE}]]`, `[[field={USER}]]`, and more.

- **Interactive Input Forms**  
  Each snippet can trigger a custom form with:
  - Multi-line text areas (`multiline: true`)
  - Editable combo boxes with presets: `[[count=10|50|100|{CLIPBOARD}]]`
  - Placeholder hints and real-time preview

- **Live Reloading**  
  Edit your `snippets.yaml` while LibreAutomate runs—changes are **automatically detected and applied** with no restart needed.

- **System Tray Integration**  
  Runs quietly in the background with a system tray icon for manual reloads or exit.

- **Silent, Custom Notifications**  
  Beautiful, soundless pop-up alerts (via dedicated UI thread) confirm when snippets are updated.

- **Trigger Flexibility**  
  Support for **multiple triggers per snippet**, including Unicode (e.g., `triggers: [":for", "Жащк"]`).

#### 🔹 **Designed for Developers**

- Use `$idx:i$` to auto-populate loop variables:  
  ```cpp
  for (int $idx:i$ = 0; $idx$ < [[count=10]]; ++$idx$) { ... }
  ```
- Paste clipboard content directly into templates with `{CLIPBOARD}`.
- Organize complex code generation without external tools.

#### 🔹 **Built for LibreAutomate**

Unlike Espanso or other snippet tools, this system **lives entirely within LibreAutomate**—leveraging its low-latency text expansion, Windows integration, and scriptability—while adding **rich form-based input**, **live reloading**, and **YAML-driven logic**.

No Electron, no Node.js, no background services—just pure, efficient C# running inside LibreAutomate.

---

> **Perfect for**: boilerplate code, email templates, log snippets, config generation, and any repetitive text pattern that deserves smart defaults and user interaction.
