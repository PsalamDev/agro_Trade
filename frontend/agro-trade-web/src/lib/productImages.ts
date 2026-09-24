// Product images provided from code — no upload needed.
//
// Drop an image file under frontend/agro-trade-web/public/ag-products/ and
// add a matching (slug prefix, path) pair below. Prefixes match the start of
// the product's URL slug, so seeded/demo slugs with hash suffixes still
// match.
//
// Example — once you have your own photos:
//   { prefix: 'palm-kernel', image: '/ag-products/palm-kernel.jpg' },
//
// A mapped image is used only when a listing has no uploaded photo. An
// uploaded cover photo always wins over these.
export const PRODUCT_IMAGE_MATCHES: ReadonlyArray<{ prefix: string; image: string }> = [
  { prefix: 'cocoa-beans', image: '/ag-products/cocoa.svg' },
  { prefix: 'cashew-nuts', image: '/ag-products/cashew.svg' },
  { prefix: 'white-maize', image: '/ag-products/produce.svg' },
  { prefix: 'arabica', image: '/ag-products/produce.svg' },
]

export function productImageFor(slug: string): string | null {
  const match = PRODUCT_IMAGE_MATCHES.find(({ prefix }) => slug.startsWith(prefix))
  return match?.image ?? null
}

export function resolveProductImage(slug: string, uploadedImageUrl: string | null): string {
  return uploadedImageUrl ?? productImageFor(slug) ?? '/ag-products/produce.svg'
}