# Comprehensive Theme Engine Architecture Plan

## 1. System Overview
We are building a dynamic theming engine that allows users to import, edit, and apply themes directly from the VS Code Marketplace, alongside a system for local user overrides.

## 2. Core Architecture
The system consists of three main layers:
1.  **Node.js Backend Proxy:** Bypasses CORS and `.vsix` archive limitations. It downloads a VS Code extension, unzips it in memory, extracts all bundled JSON/JSONC theme files, strips comments, and returns a clean token dictionary.
2.  **React Frontend Engine:** Handles fetching from the proxy, mapping VS Code's highly specific keys (e.g., `editor.background`) to our application's generic CSS variables (e.g., `--bg-primary`), and managing state.
3.  **Local Overrides & CSS Injection:** Maintains a `baseTheme` and a `userOverrides` object in local state. The engine deep-merges these (overrides win) and injects the final mapped tokens directly onto the `document.documentElement` (`:root`) as CSS variables.

## 3. Data Flow
* **Import:** User inputs an extension ID -> Proxy fetches & parses `.vsix` -> Proxy returns available themes -> User selects variant (if multiple).
* **Map:** Engine translates raw VS Code tokens to App Tokens using a predefined mapping dictionary.
* **Edit:** User tweaks a color in the Theme Editor -> Updates `userOverrides` state -> Engine merges with `baseTheme` -> Engine re-injects CSS variables to DOM for live preview.
* **CSS Architecture:** All app components use CSS variables with hardcoded fallbacks (e.g., `var(--bg-primary, #1e1e1e)`) to prevent UI breakage if an imported theme is missing specific keys.


// ==========================================
// FILE: types.ts
// ==========================================
export interface ThemeTokens {
  [key: string]: string;
}

export interface UserThemeConfig {
  baseThemeId: string | null;
  overrides: ThemeTokens;
}

// ==========================================
// FILE: themeEngine.ts (Core Logic)
// ==========================================
export function mapVSCodeToAppTokens(vsCodeTokens: ThemeTokens): ThemeTokens {
  // Map VS Code specific keys to our app's CSS variables
  const tokenMap: Record<string, string> = {
    "editor.background": "--bg-primary",
    "editor.foreground": "--text-primary",
    "activityBar.background": "--bg-sidebar",
    "button.background": "--btn-primary-bg",
    "sideBar.border": "--border-color"
    // Add additional app mappings here
  };

  const appTokens: ThemeTokens = {};
  for (const [vsKey, hexValue] of Object.entries(vsCodeTokens)) {
    const appKey = tokenMap[vsKey];
    if (appKey) appTokens[appKey] = hexValue;
  }
  return appTokens;
}

export function buildFinalTheme(baseTheme: ThemeTokens, overrides: ThemeTokens): ThemeTokens {
  return { ...baseTheme, ...overrides };
}

export function applyThemeToDOM(finalTokens: ThemeTokens) {
  const root = document.documentElement;
  for (const [cssVar, value] of Object.entries(finalTokens)) {
    const formattedVar = cssVar.startsWith('--') ? cssVar : `--${cssVar}`;
    root.style.setProperty(formattedVar, value);
  }
}

// ==========================================
// FILE: proxy.js (Node.js/Express Backend)
// Dependencies: express, axios, jszip
// ==========================================
const express = require('express');
const axios = require('axios');
const JSZip = require('jszip');
const app = express();

app.get('/api/fetch-vscode-theme', async (req, res) => {
  const { publisher, extension, version } = req.query;
  if (!publisher || !extension || !version) return res.status(400).json({ error: "Missing params" });

  const vsixUrl = `https://marketplace.visualstudio.com/_apis/public/gallery/publishers/${publisher}/vsextensions/${extension}/${version}/vspackage`;

  try {
    const response = await axios.get(vsixUrl, { responseType: 'arraybuffer' });
    const zip = await JSZip.loadAsync(response.data);
    
    const packageJsonFile = zip.file('extension/package.json');
    const packageData = JSON.parse(await packageJsonFile.async('string'));
    const themes = packageData.contributes?.themes;
    
    if (!themes || themes.length === 0) throw new Error("No themes found");

    const parsedThemes = [];
    for (const themeMeta of themes) {
      let themePath = themeMeta.path.replace(/^\.\//, '');
      const themeJsonFile = zip.file(`extension/${themePath}`);
      
      if (themeJsonFile) {
        const rawThemeString = await themeJsonFile.async('string');
        const strippedText = rawThemeString.replace(/\\"|"(?:\\"|[^"])*"|(\/\/.*|\/\*[\s\S]*?\*\/)/g, (m, g) => g ? "" : m);
        
        try {
          const themeData = JSON.parse(strippedText);
          parsedThemes.push({
            label: themeMeta.label || "Unnamed Theme",
            uiTheme: themeMeta.uiTheme,
            tokens: themeData.colors || themeData
          });
        } catch (e) {
          console.warn(`Skipped ${themeMeta.label} due to invalid JSON.`);
        }
      }
    }
    res.json({ extensionName: extension, themes: parsedThemes });
  } catch (error) {
    res.status(500).json({ error: "Failed to process VS Code extension" });
  }
});

// ==========================================
// FILE: ThemeManager.tsx (React Frontend)
// ==========================================
import React, { useState, useEffect } from 'react';
import { mapVSCodeToAppTokens, buildFinalTheme, applyThemeToDOM } from './themeEngine';

export function ThemeManager({ initialBaseTokens, initialOverrides }) {
  const [baseTokens, setBaseTokens] = useState(initialBaseTokens || {});
  const [overrides, setOverrides] = useState(initialOverrides || {});
  const [importedVariants, setImportedVariants] = useState([]);

  // Auto-apply theme when base or overrides change
  useEffect(() => {
    const finalTheme = buildFinalTheme(baseTokens, overrides);
    applyThemeToDOM(finalTheme);
  }, [baseTokens, overrides]);

  // Handle Marketplace Import
  const handleImport = async (publisher, extension, version) => {
    const res = await fetch(`/api/fetch-vscode-theme?publisher=${publisher}&extension=${extension}&version=${version}`);
    const data = await res.json();
    
    if (data.themes.length === 1) {
      applyVariant(data.themes[0].tokens);
    } else {
      setImportedVariants(data.themes);
    }
  };

  const applyVariant = (rawTokens) => {
    const mappedBase = mapVSCodeToAppTokens(rawTokens);
    setBaseTokens(mappedBase);
    setImportedVariants([]);
  };

  // Handle Local Editing
  const handleColorChange = (cssVar, newColor) => {
    setOverrides(prev => ({ ...prev, [cssVar]: newColor }));
  };

  const exportTheme = () => {
    const finalTheme = buildFinalTheme(baseTokens, overrides);
    const blob = new Blob([JSON.stringify(finalTheme, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'my-custom-theme.json';
    a.click();
  };

  return (
    <div className="theme-manager">
      <h3>Import from Marketplace</h3>
      <button onClick={() => handleImport('GitHub', 'github-vscode-theme', '6.3.0')}>
        Import GitHub Theme
      </button>

      {importedVariants.length > 0 && (
        <div className="variant-picker">
          <h4>Select variant:</h4>
          {importedVariants.map(variant => (
            <button key={variant.label} onClick={() => applyVariant(variant.tokens)}>
              {variant.label}
            </button>
          ))}
        </div>
      )}

      <h3>Live Editor</h3>
      {Object.entries(baseTokens).map(([cssVar, baseColor]) => (
        <div key={cssVar} className="color-row">
          <label>{cssVar}</label>
          <input 
            type="color" 
            value={overrides[cssVar] || baseColor} 
            onChange={(e) => handleColorChange(cssVar, e.target.value)} 
          />
        </div>
      ))}
      <button onClick={exportTheme}>Export Final Theme</button>
    </div>
  );
}

// ==========================================
// FILE: styles.css (Core Fallback Architecture)
// ==========================================
:root {
  /* Default App Theme (Fallbacks) */
  --default-bg-primary: #0d1117;
  --default-bg-sidebar: #161b22;
  --default-text-primary: #c9d1d9;
  --default-btn-primary-bg: #238636;
  --default-border-color: #30363d;
}

body {
  /* Engine dynamically overrides these variables. If a token is missing, the default is used. */
  background-color: var(--bg-primary, var(--default-bg-primary));
  color: var(--text-primary, var(--default-text-primary));
  font-family: system-ui, sans-serif;
}

.sidebar {
  background-color: var(--bg-sidebar, var(--default-bg-sidebar));
  border-right: 1px solid var(--border-color, var(--default-border-color));
}