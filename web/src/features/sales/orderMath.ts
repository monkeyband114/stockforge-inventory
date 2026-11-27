// Client-side preview of order pricing, mirroring SalesOrderLine.Price() in
// src/StockForge.Domain/Sales/SalesOrder.cs. The server recalculates and is authoritative.

/** decimal.Round(x, 2, MidpointRounding.AwayFromZero) */
export function roundMoney(amount: number): number {
  const sign = amount < 0 ? -1 : 1;
  return (sign * Math.round(Math.abs(amount) * 100 + Number.EPSILON)) / 100;
}

export interface PricedLine {
  subtotal: number;
  discount: number;
  tax: number;
  total: number;
}

export function priceLine(quantity: number, unitPrice: number, lineDiscountPercent: number, orderDiscountPercent: number, taxRate: number): PricedLine {
  if (!(quantity > 0) || !(unitPrice >= 0)) return { subtotal: 0, discount: 0, tax: 0, total: 0 };
  const subtotal = roundMoney(quantity * unitPrice);
  const lineDiscount = roundMoney((subtotal * (lineDiscountPercent || 0)) / 100);
  const afterLine = subtotal - lineDiscount;
  const orderDiscount = roundMoney((afterLine * (orderDiscountPercent || 0)) / 100);
  const net = afterLine - orderDiscount;
  const tax = roundMoney(net * (taxRate || 0));
  return { subtotal, discount: lineDiscount + orderDiscount, tax, total: net + tax };
}

export function sumLines(lines: PricedLine[]): PricedLine {
  return lines.reduce(
    (acc, l) => ({
      subtotal: roundMoney(acc.subtotal + l.subtotal),
      discount: roundMoney(acc.discount + l.discount),
      tax: roundMoney(acc.tax + l.tax),
      total: roundMoney(acc.total + l.total),
    }),
    { subtotal: 0, discount: 0, tax: 0, total: 0 },
  );
}
