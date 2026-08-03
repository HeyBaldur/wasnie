import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AssistantConversation,
  AssistantConversationSummary,
  AssistantEntitlement,
  AssistantExchange,
  AssistantStreamEvent,
} from '../models/assistant.model';

@Injectable({ providedIn: 'root' })
export class AssistantApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/assistant';

  /** "Do I get the assistant?" — any authenticated user may ask; a user without a seat gets false. */
  getEntitlement(): Observable<AssistantEntitlement> {
    return this.http.get<AssistantEntitlement>(`${this.base}/entitlement`);
  }

  listConversations(): Observable<AssistantConversationSummary[]> {
    return this.http.get<AssistantConversationSummary[]>(`${this.base}/conversations`);
  }

  getConversation(conversationId: string): Observable<AssistantConversation> {
    return this.http.get<AssistantConversation>(`${this.base}/conversations/${conversationId}`);
  }

  startConversation(title?: string): Observable<AssistantConversation> {
    return this.http.post<AssistantConversation>(`${this.base}/conversations`, { title: title ?? null });
  }

  /** The same exchange, collected. The panel uses {@link streamMessage}; this stays for non-streaming callers. */
  postMessage(conversationId: string, content: string): Observable<AssistantExchange> {
    return this.http.post<AssistantExchange>(
      `${this.base}/conversations/${conversationId}/messages`,
      { content },
    );
  }

  /**
   * The connected exchange, as Server-Sent Events.
   *
   * ★ `fetch`, not `HttpClient`: Angular's client resolves once with a whole body, which would defeat
   * the point — the answer has to arrive in pieces. The auth header is attached by hand for the same
   * reason (the interceptors sit on HttpClient), and the API KEY IS NOT INVOLVED AT ANY POINT: the
   * browser authenticates to Wasnie, and Wasnie is what holds the model's key.
   */
  async *streamMessage(
    conversationId: string,
    content: string,
    token: string | null,
    signal?: AbortSignal,
    isRetry = false,
  ): AsyncGenerator<AssistantStreamEvent> {
    const response = await fetch(`${this.base}/conversations/${conversationId}/messages/stream`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify({ content, isRetry }),
      signal,
    });

    if (!response.ok || !response.body) {
      yield { type: 'error', errorKey: 'ASSISTANT.ERROR_UNAVAILABLE' };
      return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }

      buffer += decoder.decode(value, { stream: true });

      // Frames are separated by a blank line. The tail is kept: a chunk can split a frame in half,
      // and parsing the half would drop a fragment of the answer.
      const frames = buffer.split('\n\n');
      buffer = frames.pop() ?? '';

      for (const frame of frames) {
        const line = frame.split('\n').find((l) => l.startsWith('data:'));
        if (!line) {
          continue;
        }
        try {
          yield JSON.parse(line.slice(5).trim()) as AssistantStreamEvent;
        } catch {
          // A malformed frame is skipped rather than thrown on: one bad frame must not discard the
          // answer around it.
        }
      }
    }
  }

  renameConversation(conversationId: string, title: string): Observable<AssistantConversationSummary> {
    return this.http.put<AssistantConversationSummary>(
      `${this.base}/conversations/${conversationId}`,
      { title },
    );
  }

  deleteConversation(conversationId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/conversations/${conversationId}`);
  }
}
