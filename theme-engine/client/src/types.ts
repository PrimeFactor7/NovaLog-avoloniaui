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

export interface ImportedThemeVariant {
  label: string;
  uiTheme?: string;
  tokens: ThemeTokens;
}

export interface ProxyThemeResponse {
  extensionName: string;
  themes: ImportedThemeVariant[];
  error?: string;
}
