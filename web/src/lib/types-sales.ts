// Phase 2 DTO mirrors: src/StockForge.Application/Sales/SalesContracts.cs, Billing/InvoiceService.cs,
// Dashboard/DashboardService.cs. Re-exported from ./types.
import type { Address, Guid, IsoDate, IsoDateTime, PageQuery } from "./types";

export const SALES_ORDER_STATUSES = [
  "Draft",
  "PendingApproval",
  "Approved",
  "Processing",
  "Picking",
  "Packed",
  "Shipped",
  "Delivered",
  "Cancelled",
  "Rejected",
  "PartiallyFulfilled",
  "Returned",
] as const;
export type SalesOrderStatus = (typeof SALES_ORDER_STATUSES)[number];

export const SHIPMENT_STATUSES = ["Shipped", "Delivered"] as const;
export type ShipmentStatus = (typeof SHIPMENT_STATUSES)[number];

export const INVOICE_STATUSES = ["Draft", "Issued", "PartiallyPaid", "Paid", "Overdue", "Cancelled"] as const;
export type InvoiceStatus = (typeof INVOICE_STATUSES)[number];

export const PAYMENT_METHODS = ["BankTransfer", "Card", "Cash", "Cheque", "Other"] as const;
export type PaymentMethod = (typeof PAYMENT_METHODS)[number];

// ---------- Customers ----------

export interface Customer {
  id: Guid;
  code: string;
  name: string;
  email: string | null;
  phone: string | null;
  taxNumber: string | null;
  billingAddress: Address | null;
  shippingAddress: Address | null;
  paymentTermsDays: number | null;
  creditLimit: number | null;
  notes: string | null;
  isActive: boolean;
  openBalance: number;
  orderCount: number;
  createdAt: IsoDateTime;
  version: number;
}

export interface CustomerRequest {
  name: string;
  email: string | null;
  phone: string | null;
  taxNumber: string | null;
  billingAddress: Address | null;
  shippingAddress: Address | null;
  paymentTermsDays: number | null;
  creditLimit: number | null;
  notes: string | null;
  isActive: boolean;
  version: number | null;
}

export interface CustomerQuery extends PageQuery {
  isActive?: boolean;
}

// ---------- Orders ----------

export interface OrderLineRequest {
  productId: Guid;
  quantity: number;
  unitPrice: number | null;
  discountPercent: number;
}

export interface CreateOrderRequest {
  customerId: Guid;
  warehouseId: Guid;
  requiredDate: IsoDate | null;
  customerReference: string | null;
  notes: string | null;
  discountPercent: number;
  shippingAddress: Address | null;
  lines: OrderLineRequest[];
  submit: boolean;
}

export interface UpdateOrderRequest {
  customerId: Guid;
  warehouseId: Guid;
  requiredDate: IsoDate | null;
  customerReference: string | null;
  notes: string | null;
  discountPercent: number;
  shippingAddress: Address | null;
  lines: OrderLineRequest[];
  version: number | null;
}

export interface OrderQuery extends PageQuery {
  status?: SalesOrderStatus;
  customerId?: Guid;
  warehouseId?: Guid;
  from?: IsoDate;
  to?: IsoDate;
}

export interface OrderLine {
  id: Guid;
  lineNumber: number;
  productId: Guid;
  sku: string;
  productName: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  /** Fraction 0–1. */
  taxRate: number;
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  total: number;
  quantityPicked: number;
  quantityPacked: number;
  quantityShipped: number;
  quantityReturned: number;
  quantityAvailable: number | null;
}

export interface OrderSummary {
  id: Guid;
  number: string;
  customerId: Guid;
  customerName: string;
  warehouseId: Guid;
  warehouseCode: string;
  status: SalesOrderStatus;
  orderDate: IsoDate;
  requiredDate: IsoDate | null;
  currency: string;
  total: number;
  totalQuantity: number;
  requiresApproval: boolean;
  createdAt: IsoDateTime;
}

export interface OrderInvoice {
  id: Guid;
  number: string;
  status: InvoiceStatus;
  total: number;
  balanceDue: number;
  hasPdf: boolean;
}

export interface ShipmentLine {
  salesOrderLineId: Guid;
  productId: Guid;
  sku: string;
  quantity: number;
}

export interface Shipment {
  id: Guid;
  number: string;
  salesOrderId: Guid;
  orderNumber: string;
  warehouseId: Guid;
  carrier: string | null;
  trackingNumber: string | null;
  packageCount: number;
  status: ShipmentStatus;
  shippedAt: IsoDateTime;
  deliveredAt: IsoDateTime | null;
  lines: ShipmentLine[];
}

export interface SalesReturnLine {
  salesOrderLineId: Guid;
  productId: Guid;
  sku: string;
  quantity: number;
  restock: boolean;
  creditAmount: number;
}

export interface SalesReturn {
  id: Guid;
  number: string;
  salesOrderId: Guid;
  reason: string;
  creditAmount: number;
  receivedAt: IsoDateTime;
  lines: SalesReturnLine[];
}

export interface Order {
  id: Guid;
  number: string;
  customerId: Guid;
  customerName: string;
  customerEmail: string | null;
  warehouseId: Guid;
  warehouseCode: string;
  warehouseName: string;
  status: SalesOrderStatus;
  orderDate: IsoDate;
  requiredDate: IsoDate | null;
  customerReference: string | null;
  notes: string | null;
  currency: string;
  discountPercent: number;
  shippingAddress: Address | null;
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  total: number;
  totalQuantity: number;
  requiresApproval: boolean;
  approvalReason: string | null;
  createdByName: string | null;
  createdAt: IsoDateTime;
  submittedAt: IsoDateTime | null;
  approvedByName: string | null;
  approvedAt: IsoDateTime | null;
  rejectedAt: IsoDateTime | null;
  rejectionReason: string | null;
  cancelledAt: IsoDateTime | null;
  cancellationReason: string | null;
  shippedAt: IsoDateTime | null;
  deliveredAt: IsoDateTime | null;
  lines: OrderLine[];
  invoice: OrderInvoice | null;
  shipments: Shipment[];
  returns: SalesReturn[];
  version: number;
}

export interface FulfillmentQueueItem {
  orderId: Guid;
  number: string;
  customerName: string;
  warehouseId: Guid;
  warehouseCode: string;
  status: SalesOrderStatus;
  requiredDate: IsoDate | null;
  approvedAt: IsoDateTime | null;
  totalQuantity: number;
  quantityPicked: number;
  quantityPacked: number;
  quantityShipped: number;
}

export interface RecordPickRequest {
  lines: { lineId: Guid; quantity: number }[];
}

export interface ShipOrderRequest {
  carrier: string | null;
  trackingNumber: string | null;
  packageCount: number;
}

export interface CreateReturnRequest {
  lines: { lineId: Guid; quantity: number; restock: boolean }[];
  reason: string;
}

// ---------- Billing ----------

export interface InvoiceSummary {
  id: Guid;
  number: string;
  salesOrderId: Guid | null;
  orderNumber: string | null;
  customerId: Guid;
  customerName: string;
  status: InvoiceStatus;
  issueDate: IsoDate;
  dueDate: IsoDate;
  currency: string;
  total: number;
  amountPaid: number;
  balanceDue: number;
  hasPdf: boolean;
}

export interface InvoiceLine {
  lineNumber: number;
  sku: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  taxRate: number;
  taxAmount: number;
  total: number;
}

export interface Payment {
  id: Guid;
  amount: number;
  method: PaymentMethod;
  reference: string | null;
  paidAt: IsoDateTime;
  recordedByName: string | null;
}

export interface Invoice {
  id: Guid;
  number: string;
  salesOrderId: Guid | null;
  orderNumber: string | null;
  customerId: Guid;
  status: InvoiceStatus;
  issueDate: IsoDate;
  dueDate: IsoDate;
  currency: string;
  billingName: string;
  billingEmail: string | null;
  billingTaxNumber: string | null;
  billingAddress: Address | null;
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  total: number;
  amountPaid: number;
  creditedAmount: number;
  balanceDue: number;
  hasPdf: boolean;
  pdfGeneratedAt: IsoDateTime | null;
  lines: InvoiceLine[];
  payments: Payment[];
  version: number;
}

export interface InvoiceQuery extends PageQuery {
  status?: InvoiceStatus;
  customerId?: Guid;
  outstandingOnly?: boolean;
  from?: IsoDate;
  to?: IsoDate;
}

export interface RecordPaymentRequest {
  amount: number;
  method: PaymentMethod;
  reference: string | null;
  paidAt: IsoDateTime | null;
}

export interface MonthlyFinancePoint {
  year: number;
  month: number;
  invoiced: number;
  collected: number;
}

export interface FinanceSummary {
  currency: string;
  invoicedThisMonth: number;
  collectedThisMonth: number;
  outstandingTotal: number;
  overdueTotal: number;
  outstandingInvoices: number;
  overdueInvoices: number;
  paymentsLast30Days: number;
  monthly: MonthlyFinancePoint[];
  topOverdue: InvoiceSummary[];
}

// ---------- Dashboard additions ----------

export interface MonthlySalesPoint {
  year: number;
  month: number;
  sales: number;
  orders: number;
}

export interface StatusCount {
  status: string;
  count: number;
}

export interface OrderMetrics {
  totalOrders: number;
  pendingApproval: number;
  openOrders: number;
  shippedInPeriod: number;
  salesInPeriod: number;
  fulfillmentRatePercent: number;
  averageHoursToShip: number | null;
  monthlySales: MonthlySalesPoint[];
  byStatus: StatusCount[];
}

// ---------- Realtime ----------

export interface OrderEventPayload {
  orderId: Guid;
  number: string;
  status: SalesOrderStatus;
  warehouseId: Guid;
  total: number;
}

export interface ShipmentEventPayload {
  id: Guid;
  number: string;
  salesOrderId: Guid;
  orderNumber: string;
  status: ShipmentStatus;
  trackingNumber: string | null;
}
