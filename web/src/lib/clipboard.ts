/**
 * Copies text and says whether it worked. Unlike VueUse's useClipboard there's no "copied" flag that clears itself on
 * a timer: callers show the confirmation until something changes (the app avoids timed state).
 * Falls back to a hidden textarea where the async clipboard isn't available (pages not served over https or localhost).
 */
export async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // Permission denied or not focused: try the fallback.
  }
  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.opacity = '0'
  document.body.appendChild(area)
  area.select()
  try {
    return document.execCommand('copy')
  } catch {
    return false
  } finally {
    area.remove()
  }
}
