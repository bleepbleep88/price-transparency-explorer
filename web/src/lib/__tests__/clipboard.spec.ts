import { afterEach, describe, expect, it, vi } from 'vitest'
import { copyText } from '../clipboard'

// jsdom has no async clipboard; tests add one to the real navigator and take it away again.
function setClipboard(writeText?: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, 'clipboard', { value: writeText ? { writeText } : undefined, configurable: true })
}

describe('copyText', () => {
  afterEach(() => {
    setClipboard()
  })

  it('uses the async clipboard when there is one', async () => {
    const writeText = vi.fn<(text: string) => Promise<void>>().mockResolvedValue(undefined)
    setClipboard(writeText)
    await expect(copyText('/api/v1/rates?code=45378')).resolves.toBe(true)
    expect(writeText).toHaveBeenCalledWith('/api/v1/rates?code=45378')
  })

  it('falls back to a hidden textarea and cleans it up', async () => {
    setClipboard()
    const execCommand = vi.fn<(command: string) => boolean>().mockReturnValue(true)
    document.execCommand = execCommand
    await expect(copyText('abc')).resolves.toBe(true)
    expect(execCommand).toHaveBeenCalledWith('copy')
    expect(document.querySelector('textarea')).toBeNull()
  })
})
