// ==========================================
// FILE: ThemeManager.tsx (React Frontend)
// ==========================================

import { useState, useEffect, useCallback } from 'react';
import {
  mapVSCodeToAppTokens,
  buildFinalTheme,
  applyThemeToDOM,
  getAppTokenKeys,
} from './themeEngine';
import type { ThemeTokens, ImportedThemeVariant, ProxyThemeResponse } from './types';
import { useProxyHealth } from './hooks/useProxyHealth';

const PROXY_URL = import.meta.env.VITE_PROXY_URL ?? 'http://127.0.0.1:15707';

interface ThemeManagerProps {
  initialBaseTokens?: ThemeTokens;
  initialOverrides?: ThemeTokens;
}

export function ThemeManager({
  initialBaseTokens = {},
  initialOverrides = {},
}: ThemeManagerProps) {
  const { status: proxyStatus, retry: retryProxy } = useProxyHealth(PROXY_URL);
  const [baseTokens, setBaseTokens] = useState<ThemeTokens>(initialBaseTokens);
  const [overrides, setOverrides] = useState<ThemeTokens>(initialOverrides);
  const [importedVariants, setImportedVariants] = useState<ImportedThemeVariant[]>([]);
  const [importError, setImportError] = useState<string | null>(null);
  const [importLoading, setImportLoading] = useState(false);

  // Import form state
  const [publisher, setPublisher] = useState('GitHub');
  const [extension, setExtension] = useState('github-vscode-theme');
  const [version, setVersion] = useState('6.3.0');

  // Auto-apply theme when base or overrides change
  useEffect(() => {
    const finalTheme = buildFinalTheme(baseTokens, overrides);
    applyThemeToDOM(finalTheme);
  }, [baseTokens, overrides]);

  const fetchThemes = useCallback(async () => {
    setImportError(null);
    setImportLoading(true);
    const params = new URLSearchParams({
      publisher,
      extension,
      ...(version ? { version } : {}),
    });
    try {
      const res = await fetch(`${PROXY_URL}/api/fetch-vscode-theme?${params}`);
      const data: ProxyThemeResponse = await res.json();
      if (!res.ok) {
        const msg = (data as { error?: string; message?: string }).message
          || (data as { error?: string }).error
          || `HTTP ${res.status}`;
        setImportError(msg);
        setImportedVariants([]);
        return;
      }
      if (data.themes.length === 1) {
        applyVariant(data.themes[0].tokens);
        setImportedVariants([]);
      } else {
        setImportedVariants(data.themes);
      }
    } catch (e) {
      setImportError(e instanceof Error ? e.message : 'Network error');
      setImportedVariants([]);
    } finally {
      setImportLoading(false);
    }
  }, [publisher, extension, version]);

  const applyVariant = (rawTokens: ThemeTokens) => {
    const mappedBase = mapVSCodeToAppTokens(rawTokens);
    setBaseTokens(mappedBase);
    setImportedVariants([]);
  };

  const handleColorChange = (cssVar: string, newColor: string) => {
    setOverrides((prev) => ({ ...prev, [cssVar]: newColor }));
  };

  const clearOverride = (cssVar: string) => {
    setOverrides((prev) => {
      const next = { ...prev };
      delete next[cssVar];
      return next;
    });
  };

  const exportTheme = () => {
    const finalTheme = buildFinalTheme(baseTokens, overrides);
    const blob = new Blob([JSON.stringify(finalTheme, null, 2)], {
      type: 'application/json',
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'my-custom-theme.json';
    a.click();
    URL.revokeObjectURL(url);
  };

  // Editor tokens: show all known app keys; display value = overrides[cssVar] ?? baseTokens[cssVar]
  const editorTokenKeys = getAppTokenKeys();
  const hasVariants = importedVariants.length > 0;
  const hasAnyTokens = editorTokenKeys.length > 0 || Object.keys(baseTokens).length > 0;

  return (
    <div className="theme-manager">
      {proxyStatus === 'initializing' && (
        <div className="status-banner" role="status">
          <span className="spinner" aria-hidden />
          <span>Connecting to Marketplace Engine…</span>
        </div>
      )}
      {proxyStatus === 'faulty' && (
        <div className="error-banner" role="alert">
          <p>The theme engine failed to start.</p>
          <button type="button" onClick={retryProxy}>
            Retry Connection
          </button>
        </div>
      )}
      <div
        className={`theme-manager-proxy-content${proxyStatus !== 'ready' ? ' theme-manager-proxy-content--waiting' : ''}`}
      >
        <section className="theme-manager-section gallery">
          <h3>Import from VS Code Marketplace</h3>
          <div className="import-form">
            <input
              type="text"
              placeholder="Publisher"
              value={publisher}
              onChange={(e) => setPublisher(e.target.value)}
              aria-label="Publisher"
            />
            <input
              type="text"
              placeholder="Extension"
              value={extension}
              onChange={(e) => setExtension(e.target.value)}
              aria-label="Extension"
            />
            <input
              type="text"
              placeholder="Version (optional)"
              value={version}
              onChange={(e) => setVersion(e.target.value)}
              aria-label="Version"
            />
            <button
              type="button"
              onClick={fetchThemes}
              disabled={importLoading}
            >
              {importLoading ? 'Fetching…' : 'Fetch themes'}
            </button>
          </div>
          {importError && (
            <div className="theme-manager-error" role="alert">
              {importError}
            </div>
          )}
          {hasVariants && (
            <div className="variant-picker">
              <h4>Select variant</h4>
              <div className="variant-buttons">
                {importedVariants.map((variant) => (
                  <button
                    key={variant.label}
                    type="button"
                    onClick={() => applyVariant(variant.tokens)}
                  >
                    {variant.label}
                    {variant.uiTheme ? ` (${variant.uiTheme})` : ''}
                  </button>
                ))}
            </div>
          </div>
        )}
        </section>
      </div>

      <section className="theme-manager-section editor">
        <h3>Live editor</h3>
        <p className="editor-hint">
          Override any token. Value shown is override or base; change updates override.
        </p>
        {hasAnyTokens ? (
          <div className="color-rows">
            {(Object.keys(baseTokens).length > 0 ? Object.keys(baseTokens) : editorTokenKeys)
              .filter((k) => k.startsWith('--'))
              .sort()
              .map((cssVar) => {
                const baseColor = baseTokens[cssVar];
                const overrideColor = overrides[cssVar];
                const displayValue = overrideColor ?? baseColor ?? '#000000';
                return (
                  <div key={cssVar} className="color-row">
                    <label title={cssVar}>{cssVar}</label>
                    <div className="color-row-inputs">
                      <input
                        type="color"
                        value={displayValue}
                        onChange={(e) => handleColorChange(cssVar, e.target.value)}
                        aria-label={cssVar}
                      />
                      <input
                        type="text"
                        value={displayValue}
                        onChange={(e) => handleColorChange(cssVar, e.target.value)}
                        className="color-hex"
                        aria-label={`${cssVar} hex`}
                      />
                      {overrideColor != null && (
                        <button
                          type="button"
                          className="clear-override"
                          onClick={() => clearOverride(cssVar)}
                          title="Clear override"
                        >
                          Reset
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
          </div>
        ) : (
          <p className="editor-empty">Import a theme above to see tokens, or use defaults.</p>
        )}
        <button type="button" onClick={exportTheme} className="export-btn">
          Export final theme (JSON)
        </button>
      </section>
    </div>
  );
}
