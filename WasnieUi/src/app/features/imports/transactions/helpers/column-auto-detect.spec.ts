import { detectField, TRANSACTION_FIELD_PATTERNS } from './column-auto-detect';

describe('detectField — transaction columns', () => {
  describe('referenceNumberColumn', () => {
    it('detects exact "Reference Number"', () => {
      expect(detectField(['Reference Number'], TRANSACTION_FIELD_PATTERNS['referenceNumberColumn'])).toBe('Reference Number');
    });

    it('detects "Ref No"', () => {
      expect(detectField(['Ref No'], TRANSACTION_FIELD_PATTERNS['referenceNumberColumn'])).toBe('Ref No');
    });

    it('detects "Reference" as a partial match', () => {
      expect(detectField(['Reference'], TRANSACTION_FIELD_PATTERNS['referenceNumberColumn'])).toBe('Reference');
    });

    it('returns empty string when no header matches', () => {
      expect(detectField(['Invoice', 'Order ID'], TRANSACTION_FIELD_PATTERNS['referenceNumberColumn'])).toBe('');
    });
  });

  describe('amountColumn', () => {
    it('detects exact "Amount"', () => {
      expect(detectField(['Amount'], TRANSACTION_FIELD_PATTERNS['amountColumn'])).toBe('Amount');
    });

    it('detects "Sale Amount"', () => {
      expect(detectField(['Sale Amount'], TRANSACTION_FIELD_PATTERNS['amountColumn'])).toBe('Sale Amount');
    });

    it('detects "Transaction Amount"', () => {
      expect(detectField(['Transaction Amount'], TRANSACTION_FIELD_PATTERNS['amountColumn'])).toBe('Transaction Amount');
    });

    it('detects Spanish "importe"', () => {
      expect(detectField(['Importe'], TRANSACTION_FIELD_PATTERNS['amountColumn'])).toBe('Importe');
    });
  });

  describe('currencyColumn', () => {
    it('detects "Currency"', () => {
      expect(detectField(['Currency'], TRANSACTION_FIELD_PATTERNS['currencyColumn'])).toBe('Currency');
    });

    it('detects abbreviated "Cur"', () => {
      expect(detectField(['Cur'], TRANSACTION_FIELD_PATTERNS['currencyColumn'])).toBe('Cur');
    });
  });

  describe('transactionDateColumn', () => {
    it('detects "Transaction Date"', () => {
      expect(detectField(['Transaction Date'], TRANSACTION_FIELD_PATTERNS['transactionDateColumn'])).toBe('Transaction Date');
    });

    it('detects "Date"', () => {
      expect(detectField(['Date'], TRANSACTION_FIELD_PATTERNS['transactionDateColumn'])).toBe('Date');
    });
  });

  describe('payeeCodeColumn', () => {
    it('detects "Payee Code"', () => {
      expect(detectField(['Payee Code'], TRANSACTION_FIELD_PATTERNS['payeeCodeColumn'])).toBe('Payee Code');
    });

    it('detects "Employee Code"', () => {
      expect(detectField(['Employee Code'], TRANSACTION_FIELD_PATTERNS['payeeCodeColumn'])).toBe('Employee Code');
    });
  });

  describe('externalIdColumn', () => {
    it('detects "External ID"', () => {
      expect(detectField(['External ID'], TRANSACTION_FIELD_PATTERNS['externalIdColumn'])).toBe('External ID');
    });

    it('detects "external_id"', () => {
      expect(detectField(['external_id'], TRANSACTION_FIELD_PATTERNS['externalIdColumn'])).toBe('external_id');
    });
  });

  // The quantity patterns existed long before the wizard exposed the control; now that the field is
  // mappable, these are what make the wizard suggest the column on its own.
  describe('quantityColumn', () => {
    it('detects "Quantity" (EN)', () => {
      expect(detectField(['Quantity'], TRANSACTION_FIELD_PATTERNS['quantityColumn'])).toBe('Quantity');
    });

    it('detects "Cantidad" (ES)', () => {
      expect(detectField(['Cantidad'], TRANSACTION_FIELD_PATTERNS['quantityColumn'])).toBe('Cantidad');
    });

    it('detects "Ilość" (PL)', () => {
      expect(detectField(['Ilość'], TRANSACTION_FIELD_PATTERNS['quantityColumn'])).toBe('Ilość');
    });

    it('detects the "Qty" abbreviation', () => {
      expect(detectField(['Qty'], TRANSACTION_FIELD_PATTERNS['quantityColumn'])).toBe('Qty');
    });
  });

  describe('descriptionColumn', () => {
    it('detects "Deal Name"', () => {
      expect(detectField(['Deal Name'], TRANSACTION_FIELD_PATTERNS['descriptionColumn'])).toBe('Deal Name');
    });

    it('detects "Descripción" (ES)', () => {
      expect(detectField(['Descripción'], TRANSACTION_FIELD_PATTERNS['descriptionColumn'])).toBe('Descripción');
    });

    it('detects "Opis" (PL)', () => {
      expect(detectField(['Opis'], TRANSACTION_FIELD_PATTERNS['descriptionColumn'])).toBe('Opis');
    });
  });

  describe('exact-match priority', () => {
    it('prefers exact match over partial match regardless of order', () => {
      expect(detectField(['My Amount Column', 'Amount'], TRANSACTION_FIELD_PATTERNS['amountColumn'])).toBe('Amount');
    });
  });

  describe('case-insensitivity', () => {
    it('is case-insensitive for amount', () => {
      expect(detectField(['AMOUNT'], TRANSACTION_FIELD_PATTERNS['amountColumn'])).toBe('AMOUNT');
    });
  });
});
