import { api, problemFrom } from './api'
import type { DestinationRequirementsDto } from '../types/intelligence'

/**
 * The documents a route is likely to need, from an origin/destination pair. Anonymous, like the
 * price series it sits beside.
 */
export async function getDestinationRequirements(
  origin: string,
  destination: string,
): Promise<DestinationRequirementsDto> {
  try {
    const { data } = await api.get<DestinationRequirementsDto>('/marketdata/destination-requirements', {
      params: { origin, destination },
    })
    return data
  } catch (error) {
    throw problemFrom(error)
  }
}
