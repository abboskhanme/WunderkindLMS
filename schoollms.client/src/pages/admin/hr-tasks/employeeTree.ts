/**
 * REPORTING TREE logic of the "Xodimlar" page (the old `employees/employeeTree.ts`).
 *
 * An employee may have several managers, so they appear in several places of the tree.
 * Hence both the React key and the "expanded" flag use the ANCESTOR PATH
 * (`"manager/middle/employee"`), not the `id`. Cycle-safe (A → B → A).
 */
import type { HrEmployeeNode, HrEmployeeTree } from '@/types/hrTasks'
import { hrFlattenTree } from '@/components/hr-tasks/lib'

const SEP = '/'

/** Indentation stops growing past this depth so the name column is not squeezed. */
export const HR_TREE_MAX_INDENT = 7

export interface HrTreeRow {
  employee: HrEmployeeNode
  depth: number
  path: string
  childCount: number
  hasChildren: boolean
  expanded: boolean
}

function treePath(parentPath: string | null, id: string): string {
  return parentPath ? `${parentPath}${SEP}${id}` : id
}

function visibleChildren(node: HrEmployeeNode, ancestors: ReadonlySet<string>): HrEmployeeNode[] {
  const seen = new Set<string>()
  const result: HrEmployeeNode[] = []
  for (const child of node.children ?? []) {
    if (!child?.id || ancestors.has(child.id) || seen.has(child.id)) continue
    seen.add(child.id)
    result.push(child)
  }
  return result
}

function walk(
  nodes: readonly HrEmployeeNode[] | undefined,
  parentPath: string | null,
  depth: number,
  ancestors: ReadonlySet<string>,
  expanded: ReadonlySet<string>,
  out: HrTreeRow[],
): void {
  const seen = new Set<string>()
  for (const node of nodes ?? []) {
    if (!node?.id || ancestors.has(node.id) || seen.has(node.id)) continue
    seen.add(node.id)
    const path = treePath(parentPath, node.id)
    const nextAncestors = new Set(ancestors)
    nextAncestors.add(node.id)
    const children = visibleChildren(node, nextAncestors)
    const isOpen = children.length > 0 && expanded.has(path)
    out.push({ employee: node, depth, path, childCount: children.length, hasChildren: children.length > 0, expanded: isOpen })
    if (isOpen) walk(children, path, depth + 1, nextAncestors, expanded, out)
  }
}

/** Tree → rendered rows (children of collapsed branches are skipped). */
export function hrBuildTreeRows(
  tree: HrEmployeeTree | null | undefined,
  expanded: ReadonlySet<string>,
): { roots: HrTreeRow[]; unassigned: HrTreeRow[] } {
  const roots: HrTreeRow[] = []
  const unassigned: HrTreeRow[] = []
  walk(tree?.roots, null, 0, new Set<string>(), expanded, roots)
  walk(tree?.unassigned, null, 0, new Set<string>(), expanded, unassigned)
  return { roots, unassigned }
}

/** By default only the first level is expanded. */
export function hrDefaultExpandedPaths(tree: HrEmployeeTree | null | undefined): Set<string> {
  const paths = new Set<string>()
  for (const node of tree?.roots ?? []) if (node?.id) paths.add(node.id)
  for (const node of tree?.unassigned ?? []) if (node?.id) paths.add(node.id)
  return paths
}

/** "Hammasini ochish" — the path of every node that has children. */
export function hrAllTreePaths(tree: HrEmployeeTree | null | undefined): Set<string> {
  const paths = new Set<string>()
  const collect = (nodes: readonly HrEmployeeNode[] | undefined, parentPath: string | null, ancestors: ReadonlySet<string>) => {
    const seen = new Set<string>()
    for (const node of nodes ?? []) {
      if (!node?.id || ancestors.has(node.id) || seen.has(node.id)) continue
      seen.add(node.id)
      const path = treePath(parentPath, node.id)
      const nextAncestors = new Set(ancestors)
      nextAncestors.add(node.id)
      const children = visibleChildren(node, nextAncestors)
      if (children.length === 0) continue
      paths.add(path)
      collect(children, path, nextAncestors)
    }
  }
  collect(tree?.roots, null, new Set<string>())
  collect(tree?.unassigned, null, new Set<string>())
  return paths
}

export function hrToggleTreePath(expanded: ReadonlySet<string>, path: string): Set<string> {
  const next = new Set(expanded)
  if (!next.delete(path)) next.add(path)
  return next
}

/** With a search/filter on — a FLAT list (a match must not hide in a collapsed branch). */
export function hrFlatRows(tree: HrEmployeeTree | null | undefined): HrTreeRow[] {
  return hrFlattenTree(tree).map((employee) => ({
    employee,
    depth: 0,
    path: employee.id,
    childCount: 0,
    hasChildren: false,
    expanded: false,
  }))
}

export function hrTreeSize(tree: HrEmployeeTree | null | undefined): number {
  return hrFlattenTree(tree).length
}

/** The backend's `subordinateCount` wins; otherwise the number of rendered children. */
export function hrRowSubordinates(row: Pick<HrTreeRow, 'employee' | 'childCount'>): number {
  const given = row.employee.subordinateCount
  return typeof given === 'number' && Number.isFinite(given) ? given : row.childCount
}

export function hrIndentSteps(depth: number): number {
  if (!Number.isFinite(depth) || depth <= 0) return 0
  return Math.min(Math.floor(depth), HR_TREE_MAX_INDENT)
}

/** Same manager set (order ignored)? Unchanged sets skip the `PUT managers` call. */
export function hrSameManagerIds(left: readonly string[] | null | undefined, right: readonly string[] | null | undefined): boolean {
  const a = new Set(left ?? [])
  const b = new Set(right ?? [])
  if (a.size !== b.size) return false
  for (const id of a) if (!b.has(id)) return false
  return true
}
