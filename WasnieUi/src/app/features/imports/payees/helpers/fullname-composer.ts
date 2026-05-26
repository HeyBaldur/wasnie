export function composeFullName(row: Record<string, string>, columns: string[]): string {
  return columns
    .map(col => (row[col] ?? '').trim())
    .filter(s => s.length > 0)
    .join(' ')
    .replace(/\s+/g, ' ');
}
