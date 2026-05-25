import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs))
}

const range = (from: number, to: number): number[] => {
  const pages: number[] = []
  for (let page = from; page <= to; page += 1) pages.push(page)
  return pages
}

export const getPages = (totalPages: number, currentPage: number): (number | 'SPACER')[] => {
  const totalNumbers = 5
  const totalBlocks = totalNumbers + 2

  if (totalPages <= totalBlocks) return range(1, totalPages)

  const startPage = Math.max(2, currentPage - 1)
  const endPage = Math.min(totalPages - 1, currentPage + 1)
  let pages: Array<number | 'SPACER'> = range(startPage, endPage)
  const hasLeftSpill = startPage > 2
  const hasRightSpill = totalPages - endPage > 1
  const spillOffset = totalNumbers - (pages.length + 3)

  if (hasLeftSpill && !hasRightSpill) {
    pages = ['SPACER', ...range(startPage - spillOffset, startPage - 1), ...pages]
  } else if (!hasLeftSpill && hasRightSpill) {
    pages = [...pages, ...range(endPage + 1, endPage + spillOffset), 'SPACER']
  } else {
    pages = ['SPACER', ...pages, 'SPACER']
  }

  return [1, ...pages, totalPages]
}
