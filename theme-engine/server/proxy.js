// ==========================================
// FILE: proxy.js (Node.js/Express Backend)
// Dependencies: express, axios, jszip, cors
// ==========================================

const express = require('express');
const axios = require('axios');
const JSZip = require('jszip');
const cors = require('cors');

const app = express();
app.use(cors());
app.use(express.json());

const MARKETPLACE_BASE = 'https://marketplace.visualstudio.com/_apis/public/gallery/publishers';

/**
 * Strip JSONC comments (// and /* *\/) so we can parse VS Code theme JSON.
 */
function stripJsonComments(raw) {
  return raw.replace(
    /("(?:[^"\\]|\\.)*")|(\/\/[^\n]*|\/\*[\s\S]*?\*\/)/g,
    (m, quoted, comment) => (comment ? '' : m)
  );
}

app.get('/api/fetch-vscode-theme', async (req, res) => {
  const { publisher, extension, version } = req.query;

  if (!publisher || !extension) {
    return res.status(400).json({
      error: 'Missing params',
      message: 'Required: publisher, extension. Optional: version (defaults to latest).',
    });
  }

  let resolvedVersion = version;
  if (!resolvedVersion) {
    try {
      const metaUrl = `https://marketplace.visualstudio.com/items?itemName=${encodeURIComponent(publisher + '.' + extension)}`;
      const metaRes = await axios.get(metaUrl, {
        headers: { Accept: 'application/json' },
        maxRedirects: 5,
        timeout: 10000,
        validateStatus: (s) => s === 200,
      });
      const data = metaRes.data;
      resolvedVersion = data?.versions?.[0]?.version ?? data?.version;
      if (!resolvedVersion) {
        return res.status(400).json({
          error: 'Version required',
          message: 'Could not resolve latest version. Provide publisher, extension, and version.',
        });
      }
    } catch (e) {
      return res.status(502).json({
        error: 'Marketplace lookup failed',
        message: e.response?.status ? `Marketplace returned ${e.response.status}` : e.message,
      });
    }
  }

  const vsixUrl = `${MARKETPLACE_BASE}/${encodeURIComponent(publisher)}/vsextensions/${encodeURIComponent(extension)}/${encodeURIComponent(resolvedVersion)}/vspackage`;

  try {
    const response = await axios.get(vsixUrl, {
      responseType: 'arraybuffer',
      timeout: 60000,
      maxContentLength: 100 * 1024 * 1024,
      validateStatus: (s) => s === 200,
    });

    const zip = await JSZip.loadAsync(response.data);

    const packageJsonFile = zip.file('extension/package.json');
    if (!packageJsonFile) {
      return res.status(422).json({
        error: 'Invalid VSIX',
        message: 'extension/package.json not found in package.',
      });
    }

    let packageData;
    try {
      packageData = JSON.parse(await packageJsonFile.async('string'));
    } catch (e) {
      return res.status(422).json({
        error: 'Invalid package.json',
        message: e.message,
      });
    }

    const themes = packageData.contributes?.themes;
    if (!themes || !Array.isArray(themes) || themes.length === 0) {
      return res.status(404).json({
        error: 'No themes found',
        message: 'This extension does not contribute any themes.',
      });
    }

    const parsedThemes = [];
    for (const themeMeta of themes) {
      let themePath = (themeMeta.path || '').replace(/^\.\//, '');
      if (!themePath) {
        console.warn('Theme entry missing path:', themeMeta.label);
        continue;
      }
      const themeJsonFile = zip.file(`extension/${themePath}`);
      if (!themeJsonFile) {
        console.warn('Theme file not found in VSIX:', themePath);
        continue;
      }

      const rawThemeString = await themeJsonFile.async('string');
      const strippedText = stripJsonComments(rawThemeString);

      try {
        const themeData = JSON.parse(strippedText);
        const colors = themeData.colors ?? themeData;
        if (colors && typeof colors === 'object') {
          parsedThemes.push({
            label: themeMeta.label || 'Unnamed Theme',
            uiTheme: themeMeta.uiTheme,
            tokens: colors,
          });
        } else {
          console.warn('Theme has no colors:', themeMeta.label);
        }
      } catch (e) {
        console.warn('Skipped theme due to invalid JSON:', themeMeta.label, e.message);
      }
    }

    if (parsedThemes.length === 0) {
      return res.status(422).json({
        error: 'No valid themes',
        message: 'Could not parse any theme files from this extension.',
      });
    }

    res.json({
      extensionName: extension,
      publisher,
      version: resolvedVersion,
      themes: parsedThemes,
    });
  } catch (error) {
    if (axios.isAxiosError(error)) {
      const status = error.response?.status;
      const msg = error.response?.data ?? error.message;
      if (status === 404) {
        return res.status(404).json({
          error: 'Extension or version not found',
          message: 'Check publisher, extension name, and version.',
        });
      }
      if (status && status >= 400) {
        return res.status(status).json({
          error: 'Marketplace error',
          message: typeof msg === 'string' ? msg : JSON.stringify(msg).slice(0, 200),
        });
      }
      return res.status(502).json({
        error: 'Network error',
        message: error.code || error.message,
      });
    }
    console.error('Proxy error:', error);
    res.status(500).json({
      error: 'Failed to process VS Code extension',
      message: error.message || 'Unknown error',
    });
  }
});

const PORT = process.env.PORT || 3001;
app.listen(PORT, () => {
  console.log(`Theme proxy listening on http://localhost:${PORT}`);
});
