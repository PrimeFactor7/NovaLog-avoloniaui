import { ThemeManager } from './ThemeManager';
import { ThemedLog } from './components/ThemedLog';
import './index.css';

const SAMPLE_LOG = `## Session log

| Level | Time     | Message        |
| ----- | -------- | -------------- |
| INFO  | 10:00:01 | Process started |
| WARN  | 10:00:05 | High memory use |
| ERROR | 10:00:10 | Retry failed    |

\`\`\`text
Exception: Connection refused (127.0.0.1:15707)
  at ThemeProxy.connect()
\`\`\`
`;

export default function App() {
  return (
    <div className="app">
      <header className="app-header">
        <h1>Theme Engine</h1>
        <p>Import VS Code themes, edit tokens, export. Overrides apply on top of base.</p>
      </header>
      <main className="app-main">
        <ThemeManager />
        <section className="themed-log-section">
          <h2>Themed log preview</h2>
          <p className="themed-log-hint">Markdown log viewer using theme CSS variables.</p>
          <ThemedLog content={SAMPLE_LOG} />
        </section>
      </main>
    </div>
  );
}
