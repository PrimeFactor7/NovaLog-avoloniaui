import { useState, useEffect, useCallback } from 'react';

export type ProxyStatus = 'initializing' | 'ready' | 'faulty';

const MAX_ATTEMPTS = 10;
const RETRY_DELAY_MS = 1500;
const HEALTH_TIMEOUT_MS = 1000;

export function useProxyHealth(baseUrl: string) {
  const [status, setStatus] = useState<ProxyStatus>('initializing');
  const [attempts, setAttempts] = useState(0);

  const checkPulse = useCallback(async () => {
    if (!baseUrl) {
      setStatus('faulty');
      return;
    }
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), HEALTH_TIMEOUT_MS);
      const response = await fetch(`${baseUrl}/health`, {
        signal: controller.signal,
      });
      clearTimeout(timeoutId);
      if (response.ok) {
        setStatus('ready');
      } else {
        throw new Error('Not ready');
      }
    } catch {
      if (attempts < MAX_ATTEMPTS) {
        setTimeout(() => setAttempts((a) => a + 1), RETRY_DELAY_MS);
      } else {
        setStatus('faulty');
      }
    }
  }, [baseUrl, attempts]);

  useEffect(() => {
    checkPulse();
  }, [checkPulse]);

  const retry = useCallback(() => {
    setStatus('initializing');
    setAttempts(0);
  }, []);

  return { status, retry };
}
