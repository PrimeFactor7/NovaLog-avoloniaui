import { ThemeManager } from './ThemeManager';
import './index.css';

export default function App() {
  return (
    <div className="app">
      <header className="app-header">
        <h1>Theme Engine</h1>
        <p>Import VS Code themes, edit tokens, export. Overrides apply on top of base.</p>
      </header>
      <main className="app-main">
        <ThemeManager />
      </main>
    </div>
  );
}
