# NovaLog Master Specification: Multi-Format Log Normalization & Display

## 1. Global Normalization Rules (The "Source of Truth")
All log content, regardless of origin (Windows, Linux, or Remote TCP), must be normalized upon ingestion or display based on these user-defined settings:

* **Line Endings:** Force all content to either **LF (\n)** or **CRLF (\r\n)** to prevent "stair-stepping" in Text Mode.
* **Indentation Type:** Global toggle between **Hard Tabs (\t)** and **Spaces**.
* **Indentation Size:** Configurable step size (Standard: 2 or 4).
* **Text Encoding:** Default to **UTF-8** with fallback to Windows-1252 for legacy system logs.

---

## 2. JSON Formatting Specifications
NovaLog must support two distinct viewing methods for JSON payloads detected within the "Message" column:

### Method A: Pretty-Print (Flat Text)
* **Goal:** High-speed readability without UI interaction.
* **Logic:** Use `System.Text.Json` with `WriteIndented = true`.
* **Normalization:** Apply the Global Indentation Rules (Tabs vs. Spaces) to the resulting string.
* **Visuals:** Syntax highlighting for Keys (Neon Cyan), Strings (Orange), and Numbers (Lime).

### Method B: Interactive Tree
* **Goal:** Exploration of deeply nested telemetry/objects.
* **Logic:** Map JSON nodes to a `HierarchicalTreeDataGrid` or custom `TreeView`.
* **Interaction:** Support "Expand All," "Collapse All," and "Copy Path to Key."

---

## 3. SQL Formatting Specifications
Localized SQL formatting for "Slow Query" logs or database trace files:

* **Keyword Normalization:** Automatically uppercase all SQL reserved words (`SELECT`, `FROM`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `HAVING`, `LIMIT`).
* **Line Injection:** Inject newlines before major clauses to prevent horizontal scrolling in "Span Lines" mode.
* **Indentation:** Align `AND/OR` conditions under the `WHERE` clause using the Global Indentation Step.

---

## 4. Stack Trace & Exception Formatting
Specialized formatting for .NET/Java stack traces to aid forensic analysis:

* **Header Isolation:** Bold the Exception Type and Message (e.g., **System.NullReferenceException**).
* **Path Cleaning:** Shorten long file paths (e.g., `C:\Users\Michael\Source\...` → `...\Source\...`).
* **Method Highlighting:** Use a distinct color for Method Names and Line Numbers.
* **Grouping:** Collapse repetitive "External Code" blocks to reduce noise.

---

## 5. Metadata Header Formatting (Open Folder / Folder Watch)
Specifications for the headers that sit above each file in the stream:

* **Layout:** `[Filename] | [Date Modified] | [File Size] | [Nav Arrows]`
* **Alignment:** Use a `Grid` with shared column widths so metadata stays aligned across the vertical scroll.
* **Navigation:** `Up Arrow` jumps to the previous File Header; `Down Arrow` jumps to the next.

---

## 6. Grid View "Span Lines" Integration
Specifications for the multi-line expansion logic seen in the UI:

* **Toggle Logic:** * **Single Line:** Show only the first line of the message with an ellipsis.
    * **Span Lines:** Expand the row height to fit the fully formatted/normalized JSON/SQL/Text block.
* **Max Height:** Optional "Max Row Height" with internal scrolling to prevent a single massive log from breaking the scroll feel.