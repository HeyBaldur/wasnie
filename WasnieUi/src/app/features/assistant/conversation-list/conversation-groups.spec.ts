/**
 * The two pieces of the rail redesign that carry real logic: which time band a conversation falls in,
 * and what the search box matches. The spacing, the hover and the active highlight are verified on
 * screen, not here.
 */
import {
  groupConversations,
  groupKeyFor,
} from './conversation-groups';
import { AssistantConversationSummary } from '../models/assistant.model';

/** A summary with a chosen title and update time; nothing else matters to these functions. */
function conv(id: string, title: string, updatedAt: string): AssistantConversationSummary {
  return { id, title, createdAt: updatedAt, updatedAt, messageCount: 1 };
}

/** Local-time ISO, so the tests describe the reader's calendar the way the code reads it. */
function at(year: number, month: number, day: number, hour = 12, minute = 0): string {
  return new Date(year, month - 1, day, hour, minute).toISOString();
}

describe('groupKeyFor — which band a conversation falls in', () => {
  const now = new Date(2026, 7, 18, 15, 30);   // 18 Aug 2026, 15:30 local

  it('files this calendar day under today', () => {
    expect(groupKeyFor(at(2026, 8, 18, 9), now)).toBe('today');
  });

  // ★ Calendar days, not elapsed hours. Twenty minutes before this ran, and it is still "yesterday" —
  // subtracting 24h would file it under today and contradict what the user remembers.
  it('★ calls last night yesterday, even when barely any time has passed', () => {
    const justAfterMidnight = new Date(2026, 7, 18, 0, 10);
    expect(groupKeyFor(at(2026, 8, 17, 23, 50), justAfterMidnight)).toBe('yesterday');
  });

  // ...and the mirror of it: nearly a full day ago is still TODAY when it is the same calendar day.
  it('★ keeps this morning under today even when it is nearly 24 hours old', () => {
    const lateTonight = new Date(2026, 7, 18, 23, 50);
    expect(groupKeyFor(at(2026, 8, 18, 0, 10), lateTonight)).toBe('today');
  });

  it('files the day before under yesterday', () => {
    expect(groupKeyFor(at(2026, 8, 17), now)).toBe('yesterday');
  });

  it('files two to seven days back under the previous week', () => {
    expect(groupKeyFor(at(2026, 8, 16), now)).toBe('last7');
    expect(groupKeyFor(at(2026, 8, 11), now)).toBe('last7');
  });

  it('files anything past a week under older', () => {
    expect(groupKeyFor(at(2026, 8, 10), now)).toBe('older');
    expect(groupKeyFor(at(2026, 1, 3), now)).toBe('older');
  });

  // Clock skew between the server and this machine must not exile the freshest row to the bottom.
  it('★ treats a future timestamp as today rather than older', () => {
    expect(groupKeyFor(at(2026, 8, 19), now)).toBe('today');
  });

  // A single bad row must not take the list down with it.
  it('★ survives an unparseable timestamp by sinking it to the bottom band', () => {
    expect(groupKeyFor('not a date', now)).toBe('older');
  });
});

describe('groupConversations', () => {
  const now = new Date(2026, 7, 18, 15, 30);

  it('cuts the list into bands, newest band first', () => {
    const groups = groupConversations([
      conv('old', 'Old one', at(2026, 6, 1)),
      conv('today', 'Fresh', at(2026, 8, 18)),
      conv('week', 'Midweek', at(2026, 8, 14)),
      conv('yday', 'Last night', at(2026, 8, 17)),
    ], now);

    expect(groups.map(g => g.key)).toEqual(['today', 'yesterday', 'last7', 'older']);
    expect(groups.map(g => g.items.map(i => i.id))).toEqual([['today'], ['yday'], ['week'], ['old']]);
  });

  // A heading with nothing under it is a lie about the list's shape.
  it('★ drops bands that have no conversations', () => {
    const groups = groupConversations([conv('a', 'Only one', at(2026, 8, 18))], now);

    expect(groups.map(g => g.key)).toEqual(['today']);
  });

  it('keeps the order the list arrived in within a band', () => {
    const groups = groupConversations([
      conv('first', 'First', at(2026, 8, 18, 9)),
      conv('second', 'Second', at(2026, 8, 18, 14)),
    ], now);

    expect(groups[0].items.map(i => i.id)).toEqual(['first', 'second']);
  });

  it('carries a translation key for every band it returns', () => {
    const groups = groupConversations([
      conv('a', 'A', at(2026, 8, 18)),
      conv('b', 'B', at(2026, 1, 1)),
    ], now);

    expect(groups.map(g => g.labelKey))
      .toEqual(['ASSISTANT.GROUP_TODAY', 'ASSISTANT.GROUP_OLDER']);
  });

  it('returns nothing for an empty list', () => {
    expect(groupConversations([], now)).toEqual([]);
  });
});
