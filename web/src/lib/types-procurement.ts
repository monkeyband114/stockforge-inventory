// Phase 3 DTO mirrors: src/StockForge.Application/Procurement/ProcurementContracts.cs. Re-exported from ./types.
import type { Address, Guid, IsoDate, IsoDateTime, PageQuery } from "./types";
import type { PaymentMethod } from "./types-sales";

export const PURCHASE_ORDER_STATUSES = ["Draft", "PendingApproval", "Approved", "PartiallyReceived", "Received", "Closed", "Cancelled", "Rejected"] as const;
export type PurchaseOrderStatus = (typeof PURCHASE_ORDER_STATUSES)[number];

export const SUPPLIER_INVOICE_STATUSES = ["Open", "PartiallyPaid", "Paid", "Overdue", "Cancelled"] as const;
export type SupplierInvoiceStatus = (typeof SUPPLIER_INVOICE_STATUSES)[number];

// ---------- Suppliers ----------

export interface Supplier {
  id: Guid;
  code: string;
  name: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  taxNumber: string | null;
  address: Address | null;
  paymentTermsDays: number | null;
  leadTimeDays: number | null;
  notes: string | null;
  isActive: boolean;
  productCount: number;
  openPurchaseOrders: number;
  outstandingPayables: number;
  createdAt: IsoDateTime;
  version: number;
}

export interface SupplierRequest {
  name: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  taxNumber: string | null;
  address: Address | null;
  paymentTermsDays: number | null;
  leadTimeDays: number | null;
  notes: string | null;
  isActive: boolean;
  version: number | null;
}

export interface SupplierQuery extends PageQuery {
  isActive?: boolean;
}

export interface SupplierProduct {
  productId: Guid;
  sku: string;
  productName: string;
  supplierSku: string | null;
  unitCost: number;
  minimumOrderQuantity: number;
  leadTimeDays: number | null;
  isPreferred: boolean;
}

export interface SupplierProductRequest {
  productId: Guid;
  supplierSku: string | null;
  unitCost: number;
  minimumOrderQuantity: number;
  leadTimeDays: number | null;
  isPreferred: boolean;
}

// ---------- Purchase orders ----------

export interface PurchaseOrderLineRequest {
  productId: Guid;
  quantity: number;
  unitCost: number | null;
  /** Fraction 0–1; null = 0 on the server. */
  taxRate: number | null;
}

export interface CreatePurchaseOrderRequest {
  supplierId: Guid;
  warehouseId: Guid;
  expectedDate: IsoDate | null;
  notes: string | null;
  lines: PurchaseOrderLineRequest[];
  submit: boolean;
}

export interface UpdatePurchaseOrderRequest {
  supplierId: Guid;
  warehouseId: Guid;
  expectedDate: IsoDate | null;
  notes: string | null;
  lines: PurchaseOrderLineRequest[];
  version: number | null;
}

export interface PurchaseOrderQuery extends PageQuery {
  status?: PurchaseOrderStatus;
  supplierId?: Guid;
  warehouseId?: Guid;
  outstandingOnly?: boolean;
}

export interface ReceiveGoodsRequest {
  lines: { lineId: Guid; quantity: number }[];
  deliveryNote: string | null;
  notes: string | null;
}

export interface PurchaseOrderSummary {
  id: Guid;
  number: string;
  supplierId: Guid;
  supplierName: string;
  warehouseId: Guid;
  warehouseCode: string;
  status: PurchaseOrderStatus;
  orderDate: IsoDate;
  expectedDate: IsoDate | null;
  currency: string;
  total: number;
  quantityOrdered: number;
  quantityReceived: number;
  createdAt: IsoDateTime;
}

export interface PurchaseOrderLine {
  id: Guid;
  lineNumber: number;
  productId: Guid;
  sku: string;
  productName: string;
  quantity: number;
  unitCost: number;
  taxRate: number;
  subtotal: number;
  taxAmount: number;
  total: number;
  quantityReceived: number;
  quantityOutstanding: number;
}

export interface GoodsReceiptLine {
  purchaseOrderLineId: Guid;
  productId: Guid;
  sku: string;
  quantity: number;
  unitCost: number;
}

export interface GoodsReceipt {
  id: Guid;
  number: string;
  purchaseOrderId: Guid;
  warehouseId: Guid;
  deliveryNote: string | null;
  notes: string | null;
  receivedByName: string | null;
  receivedAt: IsoDateTime;
  lines: GoodsReceiptLine[];
}

export interface PurchaseOrder {
  id: Guid;
  number: string;
  supplierId: Guid;
  supplierName: string;
  warehouseId: Guid;
  warehouseCode: string;
  status: PurchaseOrderStatus;
  orderDate: IsoDate;
  expectedDate: IsoDate | null;
  currency: string;
  notes: string | null;
  subtotal: number;
  taxTotal: number;
  total: number;
  receivedValue: number;
  requiresApproval: boolean;
  createdByName: string | null;
  /** Creator's user id — used to hide Approve (the server also enforces self_approval). */
  createdByUserId: Guid;
  createdAt: IsoDateTime;
  submittedAt: IsoDateTime | null;
  approvedByName: string | null;
  approvedAt: IsoDateTime | null;
  rejectedAt: IsoDateTime | null;
  rejectionReason: string | null;
  cancelledAt: IsoDateTime | null;
  closedAt: IsoDateTime | null;
  lines: PurchaseOrderLine[];
  receipts: GoodsReceipt[];
  invoices: SupplierInvoiceSummary[];
  version: number;
}

export interface ReorderSuggestion {
  productId: Guid;
  sku: string;
  productName: string;
  warehouseId: Guid;
  warehouseCode: string;
  quantityAvailable: number;
  reorderLevel: number;
  suggestedQuantity: number;
  quantityOnOrder: number;
  preferredSupplierId: Guid | null;
  preferredSupplierName: string | null;
  supplierUnitCost: number | null;
}

// ---------- Supplier invoices (payables) ----------

export interface CreateSupplierInvoiceRequest {
  supplierId: Guid;
  purchaseOrderId: Guid | null;
  supplierInvoiceNumber: string;
  invoiceDate: IsoDate;
  dueDate: IsoDate | null;
  subtotal: number;
  taxTotal: number;
  notes: string | null;
}

export interface SupplierInvoiceQuery extends PageQuery {
  status?: SupplierInvoiceStatus;
  supplierId?: Guid;
  outstandingOnly?: boolean;
}

export interface SupplierInvoiceSummary {
  id: Guid;
  reference: string;
  supplierInvoiceNumber: string;
  supplierId: Guid;
  supplierName: string;
  purchaseOrderId: Guid | null;
  purchaseOrderNumber: string | null;
  status: SupplierInvoiceStatus;
  invoiceDate: IsoDate;
  dueDate: IsoDate;
  currency: string;
  total: number;
  amountPaid: number;
  balanceDue: number;
  matchesReceipts: boolean;
}

export interface SupplierPayment {
  id: Guid;
  amount: number;
  method: PaymentMethod;
  reference: string | null;
  paidAt: IsoDateTime;
  recordedByName: string | null;
}

export interface SupplierInvoice {
  summary: SupplierInvoiceSummary;
  subtotal: number;
  taxTotal: number;
  notes: string | null;
  payments: SupplierPayment[];
  version: number;
}
