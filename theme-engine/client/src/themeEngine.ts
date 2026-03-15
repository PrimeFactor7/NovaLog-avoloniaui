// ==========================================
// FILE: themeEngine.ts (Core Logic)
// ==========================================

import type { ThemeTokens } from './types';

/**
 * VS Code theme key -> our app CSS variable name.
 * Expand this dictionary to support more VS Code themes.
 */
const VSCODE_TO_APP_TOKEN_MAP: Record<string, string> = {
  // Editor
  'editor.background': '--bg-primary',
  'editor.foreground': '--text-primary',
  'editor.lineHighlightBackground': '--bg-line-highlight',
  'editor.selectionBackground': '--bg-selection',
  'editorCursor.foreground': '--cursor-color',
  'editorLineNumber.foreground': '--text-line-number',
  'editorLineNumber.activeForeground': '--text-line-number-active',
  // Activity bar & sidebar
  'activityBar.background': '--bg-sidebar',
  'activityBar.foreground': '--text-sidebar',
  'sideBar.background': '--bg-sidebar',
  'sideBar.foreground': '--text-sidebar',
  'sideBar.border': '--border-color',
  'sideBarTitle.foreground': '--text-sidebar-title',
  // Title bar
  'titleBar.activeBackground': '--bg-titlebar',
  'titleBar.activeForeground': '--text-titlebar',
  'titleBar.border': '--border-color',
  // Buttons & inputs
  'button.background': '--btn-primary-bg',
  'button.foreground': '--btn-primary-fg',
  'button.hoverBackground': '--btn-primary-hover',
  'input.background': '--input-bg',
  'input.foreground': '--input-fg',
  'input.border': '--input-border',
  // Lists & trees
  'list.activeSelectionBackground': '--bg-list-selection',
  'list.activeSelectionForeground': '--text-list-selection',
  'list.hoverBackground': '--bg-list-hover',
  'list.inactiveSelectionBackground': '--bg-list-inactive-selection',
  // Scrollbar
  'scrollbarSlider.background': '--scrollbar-bg',
  'scrollbarSlider.hoverBackground': '--scrollbar-hover',
  'scrollbarSlider.activeBackground': '--scrollbar-active',
  // Status bar
  'statusBar.background': '--bg-statusbar',
  'statusBar.foreground': '--text-statusbar',
  'statusBar.border': '--border-color',
  // Tabs
  'tab.activeBackground': '--bg-tab-active',
  'tab.activeForeground': '--text-tab-active',
  'tab.inactiveBackground': '--bg-tab-inactive',
  'tab.inactiveForeground': '--text-tab-inactive',
  'tab.border': '--border-color',
  // Panel
  'panel.background': '--bg-panel',
  'panel.border': '--border-color',
  // Misc
  'focusBorder': '--focus-border',
  'foreground': '--text-primary',
  'widget.background': '--bg-widget',
};

export function mapVSCodeToAppTokens(vsCodeTokens: ThemeTokens): ThemeTokens {
  const appTokens: ThemeTokens = {};
  for (const [vsKey, hexValue] of Object.entries(vsCodeTokens)) {
    const appKey = VSCODE_TO_APP_TOKEN_MAP[vsKey];
    if (appKey && typeof hexValue === 'string') {
      appTokens[appKey] = hexValue;
    }
  }
  return appTokens;
}

/**
 * Build final theme: baseTheme merged with overrides. Overrides always win.
 * Does not mutate baseTheme.
 */
export function buildFinalTheme(baseTheme: ThemeTokens, overrides: ThemeTokens): ThemeTokens {
  return { ...baseTheme, ...overrides };
}

export function applyThemeToDOM(finalTokens: ThemeTokens): void {
  const root = document.documentElement;
  for (const [cssVar, value] of Object.entries(finalTokens)) {
    const formattedVar = cssVar.startsWith('--') ? cssVar : `--${cssVar}`;
    root.style.setProperty(formattedVar, value);
  }
}

/** Return all known app CSS variable names (for editor UI). */
export function getAppTokenKeys(): string[] {
  const keys = new Set<string>();
  for (const appKey of Object.values(VSCODE_TO_APP_TOKEN_MAP)) {
    keys.add(appKey);
  }
  return Array.from(keys).sort();
}
