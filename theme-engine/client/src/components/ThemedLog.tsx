import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import type { Components } from 'react-markdown';

/**
 * A themed Markdown log viewer that consumes CSS variables
 * injected by the Theme Engine.
 */
export function ThemedLog({ content }: { content: string }) {
  const components: Components = {
    table: ({ children, ...props }) => (
      <div style={{ overflowX: 'auto' }}>
        <table style={{ borderCollapse: 'collapse', width: '100%', margin: '12px 0' }} {...props}>
          {children}
        </table>
      </div>
    ),
    th: ({ children, ...props }) => (
      <th
        style={{
          background: 'var(--bg-secondary, var(--bg-sidebar, var(--default-bg-sidebar)))',
          borderBottom: '2px solid var(--border-subtle, var(--border-color, var(--default-border-color)))',
          padding: '8px',
          textAlign: 'left',
        }}
        {...props}
      >
        {children}
      </th>
    ),
    td: ({ children, ...props }) => (
      <td
        style={{
          borderBottom: '1px solid var(--border-subtle, var(--border-color, var(--default-border-color)))',
          padding: '8px',
        }}
        {...props}
      >
        {children}
      </td>
    ),
    code: ({ children, ...props }) => (
      <code
        style={{
          background: 'var(--bg-overlay, var(--bg-widget, var(--default-bg-widget)))',
          padding: '2px 4px',
          borderRadius: '3px',
        }}
        {...props}
      >
        {children}
      </code>
    ),
  };

  return (
    <div
      className="log-entry"
      style={{
        backgroundColor: 'var(--bg-primary, var(--default-bg-primary))',
        color: 'var(--text-primary, var(--default-text-primary))',
        padding: '16px',
        fontFamily: 'monospace',
        border: '1px solid var(--border-subtle, var(--border-color, var(--default-border-color)))',
        borderRadius: '4px',
        fontSize: '14px',
        lineHeight: '1.5',
      }}
    >
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={components}>
        {content}
      </ReactMarkdown>
    </div>
  );
}
