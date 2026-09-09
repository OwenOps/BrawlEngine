/** The catalog lives in the scrollable `<main>` of the app shell. */
export function scrollMainToTop(): void {
  const main = document.querySelector('main');
  if (main instanceof HTMLElement) {
    main.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
