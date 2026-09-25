// Product images provided from code — no upload needed.
//
// Drop an image file under frontend/agro-trade-web/public/assets/ and
// add a matching (slug prefix, path) pair below. Prefixes match the start of
// the product's URL slug, so seeded/demo slugs with hash suffixes still
// match.
//
// Example — once you have your own photos:
//   { prefix: 'palm-kernel', image: '/assets/palm-kernel.jpg' },
//
// A mapped image is used only when a listing has no uploaded photo. An
// uploaded cover photo always wins over these.
export const PRODUCT_IMAGE_MATCHES: ReadonlyArray<{ prefix: string; image: string }> = [
  { prefix: 'cocoa-beans', image: '/assets/cocoa.svg' },
  { prefix: 'cashew-nuts', image: '/assets/cashew.svg' },
  { prefix: 'palm-kernel', image: '/assets/palm-kernel.webp' },
  { prefix: 'white-maize', image: '/assets/produce.svg' },
  { prefix: 'arabica', image: '/assets/produce.svg' },
]

export function productImageFor(slug: string): string | null {
  const match = PRODUCT_IMAGE_MATCHES.find(({ prefix }) => slug.startsWith(prefix))
  return match?.image ?? null
}

export function resolveProductImage(slug: string, uploadedImageUrl: string | null): string {
  return uploadedImageUrl ?? productImageFor(slug) ?? '/assets/produce.svg'
}