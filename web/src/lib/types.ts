// TypeScript mirrors of the StockForge API DTOs (JSON is camelCase, enums are strings).
// Sources: src/StockForge.Application/**/*Contracts.cs and service files.

export type Guid = string;
/** ISO-8601 timestamp with offset (DateTimeOffset). */
export type IsoDateTime = string;
/** ISO date "yyyy-MM-dd" (DateOnly). */
export type IsoDate = string;

// ---------- Common ----------

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface PageQuery {
  page?: number;
  pageSize?: number;
  search?: string;
}

// ---------- Enums ----------

export const STOCK_MOVEMENT_TYPES = [
  "StockIn",
  "StockOut",
  "TransferOut",
  "TransferIn",
  "Adjustment",
  "Reservation",
  "Release",
  "Damaged",
  "Returned",
] as const;
export type StockMovementType = (typeof STOCK_MOVEMENT_TYPES)[number];

export const MOVEMENT_REFERENCE_TYPES = [
  "Manual",
  "StockTransfer",
  "StockAdjustment",
  "StockReservation",
  "SalesOrder",
  "SalesReturn",
  "PurchaseOrder",
  "GoodsReceipt",
] as const;
export type MovementReferenceType = (typeof MOVEMENT_REFERENCE_TYPES)[number];

export const TRANSFER_STATUSES = ["Requested", "InTransit", "Completed", "Cancelled"] as const;
export type TransferStatus = (typeof TRANSFER_STATUSES)[number];

export const ADJUSTMENT_STATUSES = ["PendingApproval", "Applied", "Rejected"] as const;
export type AdjustmentStatus = (typeof ADJUSTMENT_STATUSES)[number];

export const ADJUSTMENT_REASONS = ["CycleCount", "Correction", "Loss", "Found", "Expired", "Other"] as const;
export type AdjustmentReason = (typeof ADJUSTMENT_REASONS)[number];

export const RESERVATION_STATUSES = ["Active", "Released", "Consumed"] as const;
export type ReservationStatus = (typeof RESERVATION_STATUSES)[number];

export const WAREHOUSE_TYPES = ["DistributionCenter", "Warehouse", "RetailStore", "ThirdPartyLogistics"] as const;
export type WarehouseType = (typeof WAREHOUSE_TYPES)[number];

export const TENANT_STATUSES = ["Active", "Suspended"] as const;
export type TenantStatus = (typeof TENANT_STATUSES)[number];

export const OUTBOX_STATUSES = ["Pending", "Processing", "Succeeded", "Failed"] as const;
export type OutboxStatus = (typeof OUTBOX_STATUSES)[number];

// ---------- Identity ----------

export interface TenantInfo {
  id: Guid;
  name: string;
  slug: string;
}

export interface CurrentUser {
  id: Guid;
  email: string;
  firstName: string;
  lastName: string;
  isPlatformAdmin: boolean;
  tenant: TenantInfo | null;
  roles: string[];
  permissions: string[];
  /** null = every warehouse; otherwise the warehouses the member is assigned to. */
  warehouseIds: Guid[] | null;
  memberships: TenantInfo[];
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: IsoDateTime;
  user: CurrentUser;
  refreshToken: string | null;
  refreshTokenExpiresAt: IsoDateTime | null;
}

export interface LoginRequest {
  email: string;
  password: string;
  tenantId?: Guid | null;
}

export interface RegisterOrganizationRequest {
  organizationName: string;
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  baseCurrency?: string | null;
  timeZone?: string | null;
}

export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

export interface AcceptInvitationRequest {
  email: string;
  token: string;
  password: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface Session {
  id: Guid;
  createdAt: IsoDateTime;
  lastSeenAt: IsoDateTime;
  expiresAt: IsoDateTime;
  ipAddress: string | null;
  userAgent: string | null;
  tenantName: string | null;
  isCurrent: boolean;
}

export interface Member {
  membershipId: Guid;
  userId: Guid;
  email: string;
  firstName: string;
  lastName: string;
  jobTitle: string | null;
  isActive: boolean;
  isLockedOut: boolean;
  roles: string[];
  grantedPermissions: string[];
  warehouseIds: Guid[];
  joinedAt: IsoDateTime;
  lastLoginAt: IsoDateTime | null;
}

export interface MemberQuery extends PageQuery {
  role?: string;
  isActive?: boolean;
}

export interface InviteMemberRequest {
  email: string;
  firstName: string;
  lastName: string;
  jobTitle: string | null;
  roles: string[];
  warehouseIds: Guid[] | null;
}

export interface UpdateMemberRequest {
  jobTitle: string | null;
  roles: string[];
  warehouseIds: Guid[];
  grantedPermissions: string[];
}

export interface Role {
  name: string;
  description: string | null;
  permissions: string[];
}

export interface PermissionDefinition {
  name: string;
  group: string;
}

// ---------- Platform ----------

export interface CreateOrganizationRequest {
  organizationName: string;
  adminEmail: string;
  adminFirstName: string;
  adminLastName: string;
  baseCurrency?: string | null;
  timeZone?: string | null;
}

export interface CreatedOrganization {
  tenantId: Guid;
  name: string;
  slug: string;
  adminUserId: Guid;
  adminInvited: boolean;
}

export interface TenantSummary {
  id: Guid;
  name: string;
  slug: string;
  status: TenantStatus;
  contactEmail: string;
  baseCurrency: string;
  createdAt: IsoDateTime;
  suspendedAt: IsoDateTime | null;
  suspensionReason: string | null;
  memberCount: number;
  warehouseCount: number;
  productCount: number;
}

export interface PlatformStats {
  tenants: number;
  activeTenants: number;
  users: number;
  failedJobs: number;
  pendingJobs: number;
}

// ---------- Organization ----------

export interface Address {
  line1: string;
  line2: string | null;
  city: string;
  state: string | null;
  postalCode: string | null;
  country: string;
}

export interface TenantSettings {
  orderApprovalAmountThreshold: number;
  orderApprovalQuantityThreshold: number;
  requireApprovalForAllOrders: boolean;
  /** Fraction 0–1. */
  defaultTaxRate: number;
  invoiceDueDays: number;
  stockAdjustmentRequiresApproval: boolean;
  lowStockAlertsEnabled: boolean;
}

export interface Organization {
  id: Guid;
  name: string;
  slug: string;
  contactEmail: string;
  phone: string | null;
  address: Address | null;
  baseCurrency: string;
  timeZone: string;
  status: TenantStatus;
  settings: TenantSettings;
  createdAt: IsoDateTime;
  version: number;
}

export interface UpdateOrganizationRequest {
  name: string;
  contactEmail: string;
  phone: string | null;
  address: Address | null;
  baseCurrency: string;
  timeZone: string;
  version: number | null;
}

// ---------- Warehouses ----------

export interface Warehouse {
  id: Guid;
  code: string;
  name: string;
  type: WarehouseType;
  address: Address | null;
  phone: string | null;
  email: string | null;
  isActive: boolean;
  canOperate: boolean;
  createdAt: IsoDateTime;
  version: number;
}

export interface CreateWarehouseRequest {
  code: string;
  name: string;
  type: WarehouseType;
  address: Address | null;
  phone: string | null;
  email: string | null;
}

export interface UpdateWarehouseRequest {
  name: string;
  type: WarehouseType;
  address: Address | null;
  phone: string | null;
  email: string | null;
  isActive: boolean;
  version: number | null;
}

// ---------- Catalog ----------

export interface Unit {
  id: Guid;
  code: string;
  name: string;
  allowsFractions: boolean;
  isActive: boolean;
}

export interface CreateUnitRequest {
  code: string;
  name: string;
  allowsFractions: boolean;
}

export interface UpdateUnitRequest {
  name: string;
  allowsFractions: boolean;
  isActive: boolean;
}

export interface Category {
  id: Guid;
  name: string;
  code: string;
  description: string | null;
  parentId: Guid | null;
  isActive: boolean;
  productCount: number;
}

export interface CreateCategoryRequest {
  name: string;
  code: string;
  description: string | null;
  parentId: Guid | null;
}

export interface UpdateCategoryRequest {
  name: string;
  description: string | null;
  parentId: Guid | null;
  isActive: boolean;
}

export interface ProductUnit {
  unitId: Guid;
  unitCode: string;
  unitName: string;
  conversionFactor: number;
  barcode: string | null;
}

export interface ProductUnitRequest {
  unitId: Guid;
  conversionFactor: number;
  barcode: string | null;
}

export interface ProductListItem {
  id: Guid;
  sku: string;
  name: string;
  categoryId: Guid | null;
  categoryName: string | null;
  baseUnitCode: string;
  barcode: string | null;
  costPrice: number;
  sellingPrice: number;
  reorderLevel: number;
  isActive: boolean;
  hasImage: boolean;
  quantityOnHand: number;
  quantityAvailable: number;
}

export interface ProductStock {
  warehouseId: Guid;
  warehouseCode: string;
  warehouseName: string;
  quantityOnHand: number;
  quantityReserved: number;
  quantityAvailable: number;
  averageUnitCost: number;
}

export interface Product {
  id: Guid;
  sku: string;
  name: string;
  description: string | null;
  categoryId: Guid | null;
  categoryName: string | null;
  baseUnitId: Guid;
  baseUnitCode: string;
  barcode: string | null;
  costPrice: number;
  sellingPrice: number;
  /** Fraction 0–1. */
  taxRate: number | null;
  reorderLevel: number;
  reorderQuantity: number;
  isActive: boolean;
  hasImage: boolean;
  units: ProductUnit[];
  stock: ProductStock[];
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime | null;
  version: number;
}

export interface CreateProductRequest {
  sku: string | null;
  name: string;
  description: string | null;
  categoryId: Guid | null;
  baseUnitId: Guid;
  barcode: string | null;
  generateBarcode: boolean;
  costPrice: number;
  sellingPrice: number;
  taxRate: number | null;
  reorderLevel: number;
  reorderQuantity: number;
  units: ProductUnitRequest[] | null;
}

export interface UpdateProductRequest {
  name: string;
  description: string | null;
  categoryId: Guid | null;
  barcode: string | null;
  costPrice: number;
  sellingPrice: number;
  taxRate: number | null;
  reorderLevel: number;
  reorderQuantity: number;
  isActive: boolean;
  units: ProductUnitRequest[] | null;
  version: number | null;
}

export type ProductSort = "name" | "sku" | "price" | "-price" | "newest";

export interface ProductQuery extends PageQuery {
  categoryId?: Guid;
  isActive?: boolean;
  sort?: ProductSort;
}

// ---------- Inventory ----------

export interface InventoryQuery extends PageQuery {
  warehouseId?: Guid;
  productId?: Guid;
  categoryId?: Guid;
  lowStockOnly?: boolean;
  inStockOnly?: boolean;
}

export interface MovementQuery extends PageQuery {
  warehouseId?: Guid;
  productId?: Guid;
  type?: StockMovementType;
  referenceType?: MovementReferenceType;
  referenceId?: Guid;
  from?: IsoDateTime;
  to?: IsoDateTime;
}

export interface InventoryBalance {
  id: Guid;
  warehouseId: Guid;
  warehouseCode: string;
  warehouseName: string;
  productId: Guid;
  sku: string;
  productName: string;
  categoryName: string | null;
  unitCode: string;
  quantityOnHand: number;
  quantityReserved: number;
  quantityAvailable: number;
  reorderLevel: number;
  isLowStock: boolean;
  averageUnitCost: number;
  stockValue: number;
  lastMovementAt: IsoDateTime | null;
}

export interface StockMovement {
  id: Guid;
  createdAt: IsoDateTime;
  type: StockMovementType;
  warehouseId: Guid;
  warehouseCode: string;
  productId: Guid;
  sku: string;
  productName: string;
  quantityDelta: number;
  reservedDelta: number;
  onHandAfter: number;
  reservedAfter: number;
  unitCost: number;
  referenceType: MovementReferenceType;
  referenceId: Guid | null;
  referenceNumber: string | null;
  reason: string | null;
  createdBy: Guid | null;
  createdByName: string | null;
}

export interface WarehouseValuation {
  warehouseId: Guid;
  warehouseCode: string;
  warehouseName: string;
  units: number;
  costValue: number;
  retailValue: number;
}

export interface CategoryValuation {
  categoryId: Guid | null;
  categoryName: string;
  units: number;
  costValue: number;
  retailValue: number;
}

export interface InventoryValuation {
  totalUnits: number;
  totalCostValue: number;
  totalRetailValue: number;
  warehouses: WarehouseValuation[];
  categories: CategoryValuation[];
}

export interface StockReservation {
  id: Guid;
  warehouseId: Guid;
  warehouseCode: string;
  productId: Guid;
  sku: string;
  productName: string;
  quantity: number;
  quantityConsumed: number;
  quantityReleased: number;
  remainingQuantity: number;
  status: ReservationStatus;
  referenceType: MovementReferenceType;
  referenceId: Guid | null;
  referenceNumber: string | null;
  expiresAt: IsoDateTime | null;
  notes: string | null;
  createdAt: IsoDateTime;
}

export interface StockAdjustment {
  id: Guid;
  number: string;
  warehouseId: Guid;
  warehouseCode: string;
  productId: Guid;
  sku: string;
  productName: string;
  reason: AdjustmentReason;
  quantityChange: number;
  systemQuantity: number;
  countedQuantity: number | null;
  notes: string | null;
  status: AdjustmentStatus;
  requestedBy: Guid;
  requestedByName: string | null;
  requestedAt: IsoDateTime;
  reviewedBy: Guid | null;
  reviewedByName: string | null;
  reviewedAt: IsoDateTime | null;
  reviewNotes: string | null;
}

export interface WarehouseRef {
  id: Guid;
  code: string;
  name: string;
}

export interface StockTransferLine {
  productId: Guid;
  sku: string;
  productName: string;
  quantity: number;
  unitCost: number;
}

export interface StockTransfer {
  id: Guid;
  number: string;
  source: WarehouseRef;
  destination: WarehouseRef;
  status: TransferStatus;
  notes: string | null;
  requestedBy: Guid;
  requestedByName: string | null;
  requestedAt: IsoDateTime;
  dispatchedAt: IsoDateTime | null;
  receivedAt: IsoDateTime | null;
  cancelledAt: IsoDateTime | null;
  totalQuantity: number;
  lines: StockTransferLine[];
}

export interface StockInRequest {
  warehouseId: Guid;
  productId: Guid;
  quantity: number;
  unitId: Guid | null;
  unitCost: number | null;
  referenceNumber: string | null;
  notes: string | null;
}

export interface StockOutRequest {
  warehouseId: Guid;
  productId: Guid;
  quantity: number;
  unitId: Guid | null;
  referenceNumber: string | null;
  reason: string | null;
}

export interface DamagedStockRequest {
  warehouseId: Guid;
  productId: Guid;
  quantity: number;
  unitId: Guid | null;
  reason: string;
}

export interface ReturnedStockRequest {
  warehouseId: Guid;
  productId: Guid;
  quantity: number;
  unitId: Guid | null;
  unitCost: number | null;
  referenceNumber: string | null;
  reason: string;
}

export interface CreateReservationRequest {
  warehouseId: Guid;
  productId: Guid;
  quantity: number;
  referenceNumber: string | null;
  expiresAt: IsoDateTime | null;
  notes: string | null;
}

export interface StockCountRequest {
  warehouseId: Guid;
  productId: Guid;
  countedQuantity: number;
  notes: string | null;
}

export interface StockCorrectionRequest {
  warehouseId: Guid;
  productId: Guid;
  reason: AdjustmentReason;
  quantityChange: number;
  notes: string | null;
}

export interface ReviewAdjustmentRequest {
  notes: string | null;
}

export interface AdjustmentQuery extends PageQuery {
  warehouseId?: Guid;
  status?: AdjustmentStatus;
}

export interface TransferLineRequest {
  productId: Guid;
  quantity: number;
}

export interface CreateTransferRequest {
  sourceWarehouseId: Guid;
  destinationWarehouseId: Guid;
  lines: TransferLineRequest[];
  notes: string | null;
}

export interface TransferQuery extends PageQuery {
  warehouseId?: Guid;
  status?: TransferStatus;
}

export interface ReservationQuery extends PageQuery {
  warehouseId?: Guid;
  productId?: Guid;
  status?: ReservationStatus;
}

// ---------- Dashboards ----------

export interface TrendPoint {
  date: IsoDate;
  inbound: number;
  outbound: number;
}

export interface LowStockItem {
  productId: Guid;
  sku: string;
  name: string;
  warehouseId: Guid;
  warehouseCode: string;
  quantityAvailable: number;
  reorderLevel: number;
  reorderQuantity: number;
}

export interface OrganizationDashboard {
  totalProducts: number;
  activeProducts: number;
  totalWarehouses: number;
  inventoryCostValue: number;
  inventoryRetailValue: number;
  unitsOnHand: number;
  unitsReserved: number;
  lowStockCount: number;
  outOfStockCount: number;
  pendingAdjustments: number;
  openTransfers: number;
  movementTrend: TrendPoint[];
  valueByCategory: CategoryValuation[];
  valueByWarehouse: WarehouseValuation[];
  lowStockItems: LowStockItem[];
}

export interface WarehouseDashboard {
  warehouseId: Guid;
  code: string;
  name: string;
  skuCount: number;
  unitsOnHand: number;
  unitsReserved: number;
  unitsAvailable: number;
  stockValue: number;
  lowStockCount: number;
  incomingTransfers: number;
  incomingUnits: number;
  outgoingTransfers: number;
  outgoingUnits: number;
  pendingAdjustments: number;
  movementTrend: TrendPoint[];
  recentMovements: StockMovement[];
  lowStockItems: LowStockItem[];
}

// ---------- Audit & jobs ----------

export type JsonValue = string | number | boolean | null | JsonValue[] | { [key: string]: JsonValue };

export interface AuditLog {
  id: Guid;
  timestamp: IsoDateTime;
  userId: Guid | null;
  userEmail: string | null;
  action: string;
  entityType: string;
  entityId: string | null;
  oldValues: JsonValue | null;
  newValues: JsonValue | null;
  changedProperties: string[] | null;
  ipAddress: string | null;
  correlationId: string | null;
}

export interface AuditQuery extends PageQuery {
  entityType?: string;
  entityId?: string;
  userId?: Guid;
  action?: string;
  from?: IsoDateTime;
  to?: IsoDateTime;
}

export interface Job {
  id: Guid;
  tenantId: Guid | null;
  type: string;
  status: OutboxStatus;
  attempts: number;
  maxAttempts: number;
  createdAt: IsoDateTime;
  nextAttemptAt: IsoDateTime;
  processedAt: IsoDateTime | null;
  lastError: string | null;
  correlationId: string | null;
}

export interface JobStats {
  pending: number;
  processing: number;
  failed: number;
  succeededLast24Hours: number;
}

export interface JobQuery extends PageQuery {
  status?: OutboxStatus;
  type?: string;
}

// ---------- Realtime ----------

export interface RealtimeEnvelope<T> {
  type: string;
  tenantId: Guid;
  payload: T;
  at: IsoDateTime;
}

export interface InventoryUpdatedPayload {
  warehouseId: Guid;
  productId: Guid;
  quantityOnHand: number;
  quantityReserved: number;
  quantityAvailable: number;
}

export interface WarehouseActivityPayload {
  movementId: Guid;
  warehouseId: Guid;
  productId: Guid;
  type: StockMovementType;
  quantityDelta: number;
  reservedDelta: number;
}

export interface TransferUpdatedPayload {
  transferId: Guid;
  number: string;
  status: TransferStatus;
  sourceWarehouseId: Guid;
  destinationWarehouseId: Guid;
}

export interface AdjustmentUpdatedPayload {
  adjustmentId: Guid;
  number: string;
  warehouseId: Guid;
  productId: Guid;
  status: AdjustmentStatus;
  quantityChange: number;
}

export interface LowStockPayload {
  warehouseId: Guid;
  productId: Guid;
  sku: string;
  name: string;
  warehouseName: string;
  quantityAvailable: number;
  reorderLevel: number;
}
