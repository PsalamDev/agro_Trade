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