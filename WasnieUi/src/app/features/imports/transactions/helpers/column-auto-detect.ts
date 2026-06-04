export const TRANSACTION_FIELD_PATTERNS: Record<string, string[]> = {
  referenceNumberColumn: [
    'reference number', 'reference no', 'ref number', 'ref no', 'reference', 'ref',
    'número de referencia', 'referencia', 'numer referencyjny', 'referencja',
  ],
  amountColumn: [
    'amount', 'sale amount', 'transaction amount', 'value',
    'importe', 'monto', 'kwota', 'wartość',
  ],
  currencyColumn: [
    'currency', 'cur', 'moneda', 'divisa', 'waluta',
  ],
  transactionDateColumn: [
    'transaction date', 'sale date', 'date',
    'fecha transaccion', 'fecha de transaccion', 'fecha',
    'data transakcji', 'data',
  ],
  payeeCodeColumn: [
    'payee code', 'employee code', 'emp code', 'rep code', 'sales rep code',
    'codigo empleado', 'código de vendedor', 'kod pracownika', 'kod',
  ],
  externalIdColumn: [
    'external id', 'external_id', 'externalid', 'ext id',
    'id externo', 'id zewnętrzny',
  ],
  quantityColumn: [
    'quantity', 'qty', 'units', 'count', 'items',
    'cantidad', 'unidades', 'ilość', 'sztuki',
  ],
};

function matchesPhrase(normalized: string, pattern: string): boolean {
  if (normalized === pattern) return true;
  if (pattern.includes(' ')) return normalized.includes(pattern);
  return normalized.split(/[\s_\-]+/).includes(pattern);
}

export function detectField(headers: string[], patterns: string[]): string {
  for (const h of headers) {
    const norm = h.toLowerCase().trim();
    if (patterns.some(p => norm === p)) return h;
  }
  for (const h of headers) {
    const norm = h.toLowerCase().trim();
    if (patterns.some(p => matchesPhrase(norm, p))) return h;
  }
  return '';
}
