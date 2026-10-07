namespace AgroTrade.Domain.Enums;

// Workflow enums (plan §02). Stored as int; values are append-only, never renumbered.

public enum VerificationStatus
{
    Unverified = 0,
    Pending = 1,
    Verified = 2,
    Rejected = 3,
    Suspended = 4,
}

public enum BusinessType
{
    SoleProprietor = 0,
    Partnership = 1,
    LimitedCompany = 2,
    Cooperative = 3,
    Ngo = 4,
    Other = 5,
}

/// <summary>Direction of trade for BusinessTradeCountries reachability.</summary>
public enum TradeDirection
{
    Export = 0,
    Import = 1,
}

/// <summary>International Chamber of Commerce Incoterms 2020.</summary>
public enum Incoterms
{
    Exw = 0,
    Fca = 1,
    Fas = 2,
    Fob = 3,
    Cfr = 4,
    Cif = 5,
    Cpt = 6,
    Cip = 7,
    Dap = 8,
    Dpu = 9,
    Ddp = 10,
}

/// <summary>Bulk-trade pricing basis for listings and RFQ quantities.</summary>
public enum PriceUnit
{
    PerKilogram = 0,
    PerTon = 1,
    PerBag = 2,
    PerCarton = 3,
    PerCrate = 4,
    PerUnit = 5,
    PerLitre = 6,
    PerHead = 7,
    PerContainer = 8,
    Other = 9,
}

public enum ListingStatus
{
    Draft = 0,
    Active = 1,
    Inactive = 2,
}

/// <summary>
/// The platform's judgement of a listing, kept separate from the seller's own
/// <see cref="ListingStatus"/> (task 2.7).
/// </summary>
/// <remarks>
/// Two fields rather than one, because they answer different questions and only one of them is
/// the seller's to answer. A listing is visible when it is <c>Active</c> <i>and</i> not suspended;
/// moderation never overwrites the seller's intent, it only withholds visibility. Collapsing them
/// into one status would mean either an admin action recorded as a seller decision, or a
/// suspension a seller could clear by editing their way back to Active.
/// </remarks>
public enum ModerationStatus
{
    /// <summary>No platform action against this listing.</summary>
    Cleared = 0,

    /// <summary>Hidden from the marketplace by an administrator, pending action.</summary>
    Suspended = 1,
}

public enum RfqStatus
{
    Open = 0,
    Quoting = 1,
    Negotiating = 2,
    Accepted = 3,
    ConvertedToOrder = 4,
    Expired = 5,
    Closed = 6,
    Cancelled = 7,
}

public enum QuotationStatus
{
    Submitted = 0,
    UnderReview = 1,
    Negotiating = 2,
    Accepted = 3,
    Declined = 4,
    Withdrawn = 5,
    Expired = 6,
}

/// <summary>Order lifecycle (plan §02 — Orders). Values are append-only.</summary>
public enum OrderStatus
{
    /// <summary>Created from an accepted quotation or manual entry; nothing committed yet.</summary>
    Draft = 0,
    /// <summary>Buyer has placed the order; seller is preparing to fulfil.</summary>
    Placed = 1,
    /// <summary>Seller has confirmed and is moving the goods.</summary>
    Confirmed = 2,
    /// <summary>Goods dispatched; awaiting arrival.</summary>
    InTransit = 3,
    /// <summary>Delivered, awaiting buyer confirmation.</summary>
    Delivered = 4,
    /// <summary>Buyer confirmed receipt. Terminal-success state.</summary>
    Completed = 5,
    Cancelled = 6,
}

/// <summary>Payment state rolled up across an order's Payments (plan §02 — Payments).</summary>
public enum PaymentStatus
{
    None = 0,
    Pending = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Refunded = 4,
    Failed = 5,
}

public enum PaymentProvider
{
    /// <summary>Bank transfer / T/T / L/C recorded off-platform — no webhook, no gateway.</summary>
    ManualTransfer = 0,
    SandboxGateway = 1,
}

public enum PaymentMethod
{
    BankTransfer = 0,
    LetterOfCredit = 1,
    Gateway = 2,
}

public enum TransactionType
{
    Payment = 0,
    Refund = 1,
    Fee = 2,
}

public enum ShipmentStatus
{
    Pending = 0,
    Booked = 1,
    Departed = 2,
    InTransit = 3,
    Arrived = 4,
    Delivered = 5,
    Exception = 6,
}

public enum PaymentTerms
{
    TtAdvance = 1,
    TtPartialDeposit = 2,
    LetterOfCredit = 3,
    CashAgainstDocuments = 4,
    OnAccount = 5,
    Other = 99,
}

public enum ShipmentMode
{
    Road = 0,
    Rail = 1,
    Sea = 2,
    Air = 3,
    Multimodal = 4,
}

public enum NotificationType
{
    Order = 0,
    Rfq = 1,
    Quotation = 2,
    Message = 3,
    Payment = 4,
    Shipment = 5,
    Verification = 6,
    Document = 7,
    Dispute = 8,
    System = 9,
}

public enum MessageContextType
{
    Order = 0,
    Rfq = 1,
    Business = 2,
    General = 3,
}

public enum DocumentType
{
    // Trade documents (orders, Phase 2).
    CommercialInvoice = 0,
    PackingList = 1,
    CertificateOfOrigin = 2,
    BillOfLading = 3,
    PhytosanitaryCertificate = 4,
    InspectionCertificate = 5,
    ExportPermit = 6,
    ImportPermit = 7,
    // Business / KYC documents.
    BusinessRegistration = 10,
    TaxId = 11,
    IdDocument = 12,
    ProofOfAddress = 13,
    ProductCertification = 14,
    Other = 99,
}

public enum DocumentStatus
{
    Draft = 0,
    Uploaded = 1,
    UnderReview = 2,
    Verified = 3,
    Rejected = 4,
    Expired = 5,
}