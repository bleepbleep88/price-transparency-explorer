import { nextTick, watch, type Ref } from 'vue'

/**
 * Where focus goes back to: the element that was focused when the panel opened. When that was an item in a menu (the
 * menu is gone by then), the button that opened the menu, found by its `aria-controls`.
 */
function returnTarget(opener: HTMLElement | null): HTMLElement | null {
  if (!opener) return null
  if (opener.isConnected) return opener
  const menuId = opener.closest<HTMLElement>('[id]')?.id
  if (!menuId) return null
  return document.querySelector<HTMLElement>(`[aria-controls="${CSS.escape(menuId)}"]`)
}

/** When a drawer or dialog closes, puts focus back on what opened it, so a keyboard reader keeps their place. */
export function useReturnFocus(open: Ref<boolean>) {
  let opener: HTMLElement | null = null
  watch(open, async (isOpen) => {
    if (isOpen) {
      const active = document.activeElement
      opener = active instanceof HTMLElement && active !== document.body ? active : null
      return
    }
    const target = opener
    opener = null
    await nextTick()
    returnTarget(target)?.focus({ preventScroll: true })
  })
}
