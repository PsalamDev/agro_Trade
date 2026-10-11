// Wire mirrors of the Phase 4 destination-requirements helper (docs/02-data-model.md).
// Document types are ints on the wire, matching the rest of the trade surface.

export interface DestinationRequirementDto {
  document: number
  mandatory: boolean
  reason: string
}

export interface DestinationRequirementsDto {
  origin: string
  destination: string
  domestic: boolean
  documents: DestinationRequirementDto[]
  notes: string | null
}
