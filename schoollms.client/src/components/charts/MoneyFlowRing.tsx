/**
 * Pul aylanmasi — 3D halqa (P1-26).
 *
 * Ma'lumot TUZILISHI Alphabet'ning Sankey diagrammasidan olingan (kirim
 * manbalari → umumiy aylanma → chiqimlar + sof natija), KO'RINISHI esa
 * boshqacha: tugunlar 3D fazoda halqa bo'ylab joylashadi, bog'lanishlar —
 * egri naychalar, naychalar bo'ylab zarralar oqadi, butun sahna sekin
 * aylanadi.
 *
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ MUHIM: bu fayl `three` ni import qiladi va SHU SABABLI faqat          │
 * │ `React.lazy` orqali yuklanadi (`MoneyFlowPage.tsx`). Statik import    │
 * │ qilinsa, three (~450 kB) butun moliya bo'limining bundle'iga tushadi  │
 * │ va bu sahifani hech qachon ochmagan foydalanuvchi ham uni yuklaydi.   │
 * └──────────────────────────────────────────────────────────────────────┘
 *
 * <b>Nega `MeshBasicMaterial` va yorug'lik yo'q.</b> Sahna qorong'i fonda
 * yorqin naychalardan iborat — bu yerda yoritish modeli hech narsa qo'shmaydi,
 * faqat har materialga qo'shimcha shader varianti va draw-call qo'shardi.
 * Chuqurlik hissi perspektiva, aylanish va additive blending bilan beriladi.
 *
 * <b>`prefers-reduced-motion`.</b> Yoqilgan bo'lsa: zarralar UMUMAN
 * yaratilmaydi, aylanish yo'q, `requestAnimationFrame` tsikli yurmaydi —
 * sahna faqat kerak bo'lganda (o'lcham o'zgarganda, kursor tekkanda) qayta
 * chiziladi. Ya'ni "sekinlashtirilgan animatsiya" emas, animatsiyaning
 * YO'QLIGI.
 */
import { useCallback, useEffect, useRef, useState } from 'react'
import {
  AdditiveBlending,
  BufferAttribute,
  BufferGeometry,
  CanvasTexture,
  Color,
  CubicBezierCurve3,
  Group,
  Mesh,
  MeshBasicMaterial,
  PerspectiveCamera,
  Points,
  PointsMaterial,
  Raycaster,
  Scene,
  SphereGeometry,
  Sprite,
  SpriteMaterial,
  TorusGeometry,
  TubeGeometry,
  Vector2,
  Vector3,
  WebGLRenderer,
} from 'three'
import {
  formatSom,
  type MoneyFlow,
  type MoneyFlowKind,
  type MoneyFlowNode,
} from '@/api/services/moneyFlow'

/* ------------------------------------------------------------------ */
/*  Ranglar — loyihadagi mavjud qiymatlar                              */
/*  (brand-500 `index.css` dan; yashil/qizil `FinanceMonthlyChart` dan) */
/* ------------------------------------------------------------------ */
const COLOR_INCOME = 0x16a34a
const COLOR_EXPENSE = 0xdc2626
const COLOR_HUB = 0x3366ff
const COLOR_PROFIT = 0xf59e0b
const COLOR_RING = 0x334155

/** Halqa radiusi (sahna birligida). */
const RING_RADIUS = 5

/** Kirim yoyi — chap yarim doira; chiqim yoyi — o'ng. Ular QARAMA-QARSHI. */
const INCOME_ARC: [number, number] = [Math.PI * 0.6, Math.PI * 1.4]
const EXPENSE_ARC: [number, number] = [-Math.PI * 0.4, Math.PI * 0.4]

/** Naychaning markazdan yuqoriga (kirim) / pastga (chiqim) egilishi. */
const ARC_LIFT = 1.7

/** Zarralarning umumiy chegarasi — sahna qancha katta bo'lmasin, shundan oshmaydi. */
const PARTICLE_BUDGET = 420

/** Bir aylanish uchun ~52 soniya: sezilarli, lekin chalg'itmaydi. */
const SPIN_RAD_PER_SEC = 0.12

interface HoverInfo {
  label: string
  value: number
  kind: MoneyFlowKind | 'link'
  /** Bog'lanish uchun: "O'quv to'lovi → Umumiy aylanma" */
  detail?: string
}

interface Props {
  flow: MoneyFlow
}

/* ------------------------------------------------------------------ */
/*  Yordamchilar                                                       */
/* ------------------------------------------------------------------ */

/** Yoy bo'ylab n ta burchak; bitta bo'lsa — yoyning o'rtasi. */
function arcAngles(n: number, [start, end]: [number, number]): number[] {
  if (n <= 0) return []
  if (n === 1) return [(start + end) / 2]
  const step = (end - start) / (n - 1)
  return Array.from({ length: n }, (_, i) => start + i * step)
}

/** Halqadagi nuqta (XZ tekisligi, y = 0). */
function ringPoint(angle: number): Vector3 {
  return new Vector3(Math.cos(angle) * RING_RADIUS, 0, Math.sin(angle) * RING_RADIUS)
}

/** Ikki nuqta orasidagi egri naycha o'qi. `lift` — egilish balandligi. */
function flowCurve(from: Vector3, to: Vector3, lift: number): CubicBezierCurve3 {
  const c1 = from.clone().lerp(to, 0.3)
  const c2 = from.clone().lerp(to, 0.7)
  c1.y += lift
  c2.y += lift * 0.62
  return new CubicBezierCurve3(from, c1, c2, to)
}

/** Yorliqlardagi qisqa summa: "12,4 mln". Aniq raqam kursor tekkanda chiqadi. */
function shortSom(value: number): string {
  const abs = Math.abs(value)
  if (abs >= 1e9) return `${(value / 1e9).toFixed(1).replace('.', ',')} mlrd`
  if (abs >= 1e6) return `${(value / 1e6).toFixed(1).replace('.', ',')} mln`
  if (abs >= 1e3) return `${Math.round(value / 1e3)} ming`
  return `${Math.round(value)}`
}

/** Kichik oqim ham ko'rinsin: kvadrat ildiz bilan siqilgan nisbat. */
function scaleShare(value: number, max: number): number {
  if (max <= 0) return 0
  return Math.sqrt(Math.min(Math.abs(value) / max, 1))
}

/** Zarra uchun yumaloq nuqta teksturasi (radial gradient). */
function makeDotTexture(): CanvasTexture {
  const size = 64
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  const ctx = canvas.getContext('2d')
  if (ctx) {
    const g = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2)
    g.addColorStop(0, 'rgba(255,255,255,1)')
    g.addColorStop(0.35, 'rgba(255,255,255,0.85)')
    g.addColorStop(1, 'rgba(255,255,255,0)')
    ctx.fillStyle = g
    ctx.fillRect(0, 0, size, size)
  }
  return new CanvasTexture(canvas)
}

/**
 * Matn yorlig'i — canvas teksturali sprite. Sprite har doim kameraga qaraydi,
 * shuning uchun sahna aylanganda ham o'qiladi.
 *
 * HTML overlay emas: overlay har kadrda `style.transform` yozishni talab
 * qilardi (loyihada inline style ishlatilmaydi) va yorliq 3D chuqurligini
 * yo'qotardi.
 *
 * <b>`sizeAttenuation: false` — muhim.</b> Standart holatda sprite
 * perspektiva bo'yicha kichrayadi: halqaning oldingi yoyidagi yorliq ulkan,
 * orqa yoyidagisi esa o'qib bo'lmas darajada kichik chiqadi va aylanish
 * davomida ular doim "nafas olib" turadi. Bu bayroq o'lchamni EKRAN
 * bo'yicha qat'iy qiladi — matn hamma joyda bir xil o'qiladi, chuqurlikni
 * esa naychalar va zarralar beradi.
 */
function makeLabelSprite(title: string, subtitle: string): Sprite {
  const scale = 2
  const width = 320 * scale
  const height = 96 * scale
  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height

  const ctx = canvas.getContext('2d')
  if (ctx) {
    ctx.scale(scale, scale)
    ctx.textAlign = 'center'
    ctx.shadowColor = 'rgba(2, 6, 23, 0.9)'
    ctx.shadowBlur = 6

    ctx.font = "600 30px Inter, system-ui, 'Segoe UI', sans-serif"
    ctx.fillStyle = '#e2e8f0'
    ctx.fillText(title, 160, 36, 300)

    ctx.font = "500 26px Inter, system-ui, 'Segoe UI', sans-serif"
    ctx.fillStyle = '#94a3b8'
    ctx.fillText(subtitle, 160, 72, 300)
  }

  const texture = new CanvasTexture(canvas)
  const sprite = new Sprite(
    new SpriteMaterial({
      map: texture,
      transparent: true,
      depthWrite: false,
      sizeAttenuation: false,
    }),
  )
  sprite.scale.set(LABEL_SCALE, LABEL_SCALE / LABEL_ASPECT, 1)
  return sprite
}

/** Ekran bo'yicha qat'iy yorliq kengligi (`sizeAttenuation: false` birligida). */
const LABEL_SCALE = 0.23
/** Yorliq canvas'ining nisbati (320 × 96). */
const LABEL_ASPECT = 320 / 96

/* ------------------------------------------------------------------ */
/*  Komponent                                                          */
/* ------------------------------------------------------------------ */

export function MoneyFlowRing({ flow }: Props) {
  const hostRef = useRef<HTMLDivElement>(null)
  const [hover, setHover] = useState<HoverInfo | null>(null)
  const [webglFailed, setWebglFailed] = useState(false)

  // `prefers-reduced-motion` — boshlang'ich qiymat ham, keyingi o'zgarishi ham.
  const [reducedMotion, setReducedMotion] = useState(
    () => window.matchMedia('(prefers-reduced-motion: reduce)').matches,
  )

  useEffect(() => {
    const mq = window.matchMedia('(prefers-reduced-motion: reduce)')
    const onChange = (e: MediaQueryListEvent) => setReducedMotion(e.matches)
    mq.addEventListener('change', onChange)
    return () => mq.removeEventListener('change', onChange)
  }, [])

  const build = useCallback(
    (host: HTMLDivElement) => {
      let renderer: WebGLRenderer
      try {
        renderer = new WebGLRenderer({
          antialias: true,
          alpha: true,
          powerPreference: 'high-performance',
        })
      } catch {
        setWebglFailed(true)
        return undefined
      }

      setWebglFailed(false)

      const canvas = renderer.domElement
      // Tailwind class — `setSize(..., false)` bilan birga inline style
      // umuman yozilmaydi (three aks holda canvas.style ni o'zi to'ldiradi).
      canvas.className = 'block h-full w-full'
      renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2))
      host.appendChild(canvas)

      const scene = new Scene()
      const camera = new PerspectiveCamera(42, 1, 0.1, 100)
      camera.position.set(0, 4.0, 10.0)
      // Nigoh markazdan bir oz PASTDA: naychalar yuqoriga ham, pastga ham
      // egiladi, lekin yorliqlar faqat yuqorida — shu siljish sahnani
      // kartochka ichida ko'z bilan markazlashtiradi.
      camera.lookAt(0, -0.25, 0)

      /** Aylanadigan guruh — kamera qimirlamaydi, sahna aylanadi. */
      const spinner = new Group()
      scene.add(spinner)

      // ---- Tugun joylashuvi (halqa bo'ylab) ----
      const nodeById = new Map<string, MoneyFlowNode>(flow.nodes.map((n) => [n.id, n]))
      const incomingLinks = flow.links.filter((l) => l.target === 'hub')
      const outgoingLinks = flow.links.filter((l) => l.source === 'hub')

      const positions = new Map<string, Vector3>()
      positions.set('hub', new Vector3(0, 0, 0))

      arcAngles(incomingLinks.length, INCOME_ARC).forEach((angle, i) => {
        positions.set(incomingLinks[i].source, ringPoint(angle))
      })
      arcAngles(outgoingLinks.length, EXPENSE_ARC).forEach((angle, i) => {
        positions.set(outgoingLinks[i].target, ringPoint(angle))
      })

      const maxLink = Math.max(...flow.links.map((l) => Math.abs(l.value)), 1)
      const totalAbs = flow.links.reduce((sum, l) => sum + Math.abs(l.value), 0) || 1

      const disposables: Array<{ dispose: () => void }> = []
      const track = <T extends { dispose: () => void }>(item: T): T => {
        disposables.push(item)
        return item
      }

      const colorOf = (node: MoneyFlowNode): number => {
        if (node.kind === 'income') return COLOR_INCOME
        if (node.kind === 'expense') return COLOR_EXPENSE
        if (node.kind === 'hub') return COLOR_HUB
        // Sof natija: foyda — sariq (chiquvchi), kamomad — qizil (kiruvchi).
        return incomingLinks.some((l) => l.source === node.id) ? COLOR_EXPENSE : COLOR_PROFIT
      }

      // ---- Yo'naltiruvchi halqa (ingichka torus) ----
      const ringMesh = new Mesh(
        track(new TorusGeometry(RING_RADIUS, 0.014, 6, 180)),
        track(new MeshBasicMaterial({ color: COLOR_RING, transparent: true, opacity: 0.75 })),
      )
      ringMesh.rotation.x = Math.PI / 2
      spinner.add(ringMesh)

      // ---- Tugunlar: shar + halo + yorliq ----
      const pickable: Mesh[] = []

      for (const node of flow.nodes) {
        const pos = positions.get(node.id)
        if (!pos) continue

        const color = colorOf(node)
        const radius =
          node.kind === 'hub' ? 0.8 : 0.18 + 0.34 * scaleShare(node.value, maxLink)

        const mesh = new Mesh(
          track(new SphereGeometry(radius, 28, 20)),
          track(new MeshBasicMaterial({ color })),
        )
        mesh.position.copy(pos)
        mesh.userData = {
          hover: {
            label: node.label,
            value: node.value,
            kind: node.kind,
          } satisfies HoverInfo,
          baseColor: color,
        }
        spinner.add(mesh)
        pickable.push(mesh)

        const halo = new Mesh(
          track(new SphereGeometry(radius * 1.55, 20, 14)),
          track(
            new MeshBasicMaterial({
              color,
              transparent: true,
              opacity: 0.12,
              depthWrite: false,
              blending: AdditiveBlending,
            }),
          ),
        )
        halo.position.copy(pos)
        spinner.add(halo)

        const label = makeLabelSprite(node.label, `${shortSom(node.value)} so'm`)
        if (node.kind === 'hub') {
          label.position.set(0, radius + 0.95, 0)
          label.scale.set(LABEL_SCALE * 1.3, (LABEL_SCALE * 1.3) / LABEL_ASPECT, 1)
        } else {
          label.position.copy(pos.clone().multiplyScalar(1.22).setY(radius + 0.85))
        }
        spinner.add(label)
        if (label.material.map) disposables.push(label.material.map)
        disposables.push(label.material)
      }

      // ---- Bog'lanishlar: egri naychalar ----
      const curves: CubicBezierCurve3[] = []
      const linkShares: number[] = []
      const linkColors: Color[] = []

      for (const link of flow.links) {
        const from = positions.get(link.source)
        const to = positions.get(link.target)
        if (!from || !to) continue

        const incoming = link.target === 'hub'
        const leafId = incoming ? link.source : link.target
        const leaf = nodeById.get(leafId)
        const color = leaf ? colorOf(leaf) : COLOR_HUB
        const curve = flowCurve(from, to, incoming ? ARC_LIFT : -ARC_LIFT)

        const tubeRadius = 0.045 + 0.2 * scaleShare(link.value, maxLink)
        const tube = new Mesh(
          track(new TubeGeometry(curve, 72, tubeRadius, 10, false)),
          track(
            new MeshBasicMaterial({
              color,
              transparent: true,
              opacity: 0.38,
              depthWrite: false,
            }),
          ),
        )
        tube.userData = {
          hover: {
            label: leaf?.label ?? link.source,
            value: link.value,
            kind: 'link',
            detail: incoming
              ? `${leaf?.label ?? link.source} → Umumiy aylanma`
              : `Umumiy aylanma → ${leaf?.label ?? link.target}`,
          } satisfies HoverInfo,
          baseColor: color,
        }
        spinner.add(tube)
        pickable.push(tube)

        curves.push(curve)
        linkShares.push(Math.abs(link.value) / totalAbs)
        linkColors.push(new Color(color))
      }

      // ---- Zarralar (naychalar bo'ylab oqim) ----
      // Zichlik VA tezlik summaga proporsional: katta oqim — ko'p va tez
      // nuqtalar, kichik oqim — siyrak va sekin. Ikkalasi ham bitta
      // `share` dan kelib chiqadi, ya'ni ekranda ko'rilgan "og'irlik"
      // raqamning o'ziga bog'liq.
      let particles: Points | null = null
      let particleCurve: Int32Array = new Int32Array(0)
      let particleT = new Float32Array(0)
      let particleSpeed = new Float32Array(0)

      if (!reducedMotion && curves.length > 0) {
        const counts = linkShares.map((share) =>
          Math.max(3, Math.round(PARTICLE_BUDGET * share)),
        )
        const total = counts.reduce((a, b) => a + b, 0)

        const positionsArray = new Float32Array(total * 3)
        const colorsArray = new Float32Array(total * 3)
        particleCurve = new Int32Array(total)
        particleT = new Float32Array(total)
        particleSpeed = new Float32Array(total)

        const maxShare = Math.max(...linkShares, 1e-9)
        let cursor = 0
        counts.forEach((count, linkIndex) => {
          const speed = 0.05 + 0.13 * (linkShares[linkIndex] / maxShare)
          const color = linkColors[linkIndex]
          for (let i = 0; i < count; i += 1) {
            particleCurve[cursor] = linkIndex
            // Teng oraliqda + kichik siljish: nuqtalar "poyezd" bo'lib
            // qolmasin, oqim tabiiy ko'rinsin.
            particleT[cursor] = (i / count + Math.random() * 0.02) % 1
            particleSpeed[cursor] = speed
            colorsArray[cursor * 3] = color.r
            colorsArray[cursor * 3 + 1] = color.g
            colorsArray[cursor * 3 + 2] = color.b
            cursor += 1
          }
        })

        const geometry = track(new BufferGeometry())
        geometry.setAttribute('position', new BufferAttribute(positionsArray, 3))
        geometry.setAttribute('color', new BufferAttribute(colorsArray, 3))

        const dot = track(makeDotTexture())
        particles = new Points(
          geometry,
          track(
            new PointsMaterial({
              size: 0.17,
              map: dot,
              vertexColors: true,
              transparent: true,
              depthWrite: false,
              blending: AdditiveBlending,
              sizeAttenuation: true,
            }),
          ),
        )
        spinner.add(particles)
      }

      // ---- O'lcham ----
      const resize = () => {
        const width = host.clientWidth || 1
        const height = host.clientHeight || 1
        camera.aspect = width / height
        camera.updateProjectionMatrix()
        renderer.setSize(width, height, false)
      }
      resize()

      const observer = new ResizeObserver(() => {
        resize()
        renderer.render(scene, camera)
      })
      observer.observe(host)

      // ---- Kursor ----
      const pointer = new Vector2()
      const raycaster = new Raycaster()
      let pointerActive = false
      let highlighted: Mesh | null = null

      const white = new Color(0xffffff)

      /**
       * Kursor ostidagi obyektni topadi va FAQAT o'zgargandagina React
       * holatiga tegadi. Har kadrda `setHover` chaqirish butun sahifani
       * sekundiga 60 marta qayta render qilardi — bu 3D sahnadan ham
       * qimmatroq tushardi.
       */
      const pick = () => {
        let mesh: Mesh | null = null
        if (pointerActive) {
          raycaster.setFromCamera(pointer, camera)
          const hit = raycaster.intersectObjects(pickable, false)[0]
          mesh = (hit?.object as Mesh | undefined) ?? null
        }
        if (mesh === highlighted) return

        if (highlighted) {
          const material = highlighted.material as MeshBasicMaterial
          material.color.setHex(highlighted.userData.baseColor as number)
        }
        if (mesh) {
          const material = mesh.material as MeshBasicMaterial
          material.color.setHex(mesh.userData.baseColor as number).lerp(white, 0.45)
        }
        highlighted = mesh
        canvas.classList.toggle('cursor-pointer', mesh !== null)
        setHover(mesh ? (mesh.userData.hover as HoverInfo) : null)
      }

      const onPointerMove = (event: PointerEvent) => {
        const rect = canvas.getBoundingClientRect()
        pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1
        pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1
        pointerActive = true
        if (reducedMotion) {
          pick()
          renderer.render(scene, camera)
        }
      }

      const onPointerLeave = () => {
        pointerActive = false
        if (reducedMotion) {
          pick()
          renderer.render(scene, camera)
        }
      }

      canvas.addEventListener('pointermove', onPointerMove)
      canvas.addEventListener('pointerleave', onPointerLeave)

      // ---- Tsikl ----
      let frame = 0
      if (reducedMotion) {
        // Harakat yo'q: bir marta chiziladi, keyin faqat hodisalarda.
        renderer.render(scene, camera)
      } else {
        const point = new Vector3()
        let previous = performance.now()
        let frameCount = 0

        const tick = (now: number) => {
          frame = requestAnimationFrame(tick)
          // Tab orqa fonda turgandan keyin sakrab ketmasin.
          const delta = Math.min((now - previous) / 1000, 0.1)
          previous = now
          frameCount += 1

          spinner.rotation.y += SPIN_RAD_PER_SEC * delta

          if (particles) {
            const attribute = particles.geometry.getAttribute('position') as BufferAttribute
            const array = attribute.array as Float32Array
            for (let i = 0; i < particleT.length; i += 1) {
              let t = particleT[i] + particleSpeed[i] * delta
              if (t > 1) t -= 1
              particleT[i] = t
              // `getPoint` (getPointAt emas): yoy uzunligi jadvalisiz,
              // to'g'ridan-to'g'ri ko'phad hisobi — kadrga ~400 ta chaqiruv arzon.
              curves[particleCurve[i]].getPoint(t, point)
              array[i * 3] = point.x
              array[i * 3 + 1] = point.y
              array[i * 3 + 2] = point.z
            }
            attribute.needsUpdate = true
          }

          // Nur tekshiruvi (raycast) — har kadrda emas, har uchinchisida.
          // Sahna aylanayotgani uchun kursor qimirlamasa ham ostidagi obyekt
          // o'zgaradi, ya'ni butunlay to'xtatib bo'lmaydi; ~20 Hz esa qo'l
          // uchun sezilmaydi va naychalarning uchburchaklarini uch barobar
          // kam tekshiradi.
          if (frameCount % 3 === 0) pick()
          renderer.render(scene, camera)
        }

        frame = requestAnimationFrame(tick)
      }

      return () => {
        if (frame) cancelAnimationFrame(frame)
        observer.disconnect()
        canvas.removeEventListener('pointermove', onPointerMove)
        canvas.removeEventListener('pointerleave', onPointerLeave)
        for (const item of disposables) item.dispose()
        renderer.dispose()
        canvas.remove()
      }
    },
    [flow, reducedMotion],
  )

  useEffect(() => {
    const host = hostRef.current
    if (!host) return
    setHover(null)
    return build(host)
  }, [build])

  const readout = hover

  return (
    <div className="relative">
      <div
        ref={hostRef}
        role="img"
        aria-label="Maktab pul aylanmasining 3D halqa ko'rinishi. Aniq raqamlar quyidagi jadvalda."
        className="h-[26rem] w-full overflow-hidden rounded-2xl bg-slate-900 sm:h-[32rem]"
      />

      {webglFailed && (
        <div className="absolute inset-0 flex items-center justify-center rounded-2xl bg-slate-900 p-6">
          <p className="max-w-sm text-center text-sm text-slate-300">
            Bu qurilma/brauzer 3D grafikani (WebGL) qo'llab-quvvatlamayapti. Barcha raqamlar
            quyidagi jadvalda to'liq ko'rsatilgan.
          </p>
        </div>
      )}

      {!webglFailed && (
        <>
          {/* Izoh (legend) — halqadagi ranglar nimani bildiradi. */}
          <div className="pointer-events-none absolute left-4 top-4 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-400">
            <span className="flex items-center gap-1.5">
              <span className="h-2 w-2 rounded-full bg-green-600" /> Kirim
            </span>
            <span className="flex items-center gap-1.5">
              <span className="h-2 w-2 rounded-full bg-brand-500" /> Umumiy aylanma
            </span>
            <span className="flex items-center gap-1.5">
              <span className="h-2 w-2 rounded-full bg-red-600" /> Chiqim
            </span>
            <span className="flex items-center gap-1.5">
              <span className="h-2 w-2 rounded-full bg-amber-500" /> Sof natija
            </span>
          </div>

          {/* Kursor ostidagi tugun/bog'lanish — ANIQ raqam bilan. */}
          <div className="pointer-events-none absolute inset-x-4 bottom-4">
            {readout ? (
              <div className="inline-block rounded-xl border border-slate-700 bg-slate-800/90 px-4 py-2.5">
                <p className="text-xs text-slate-400">
                  {readout.detail ?? (readout.kind === 'hub' ? 'Markaziy tugun' : 'Tugun')}
                </p>
                <p className="text-sm font-semibold text-slate-100">
                  {readout.label} — {formatSom(readout.value)}
                </p>
              </div>
            ) : (
              <p className="text-xs text-slate-500">
                {reducedMotion
                  ? "Harakatni kamaytirish rejimi yoqilgan — halqa statik ko'rsatilmoqda."
                  : 'Aniq summani ko’rish uchun tugun yoki naycha ustiga kursorni olib boring.'}
              </p>
            )}
          </div>
        </>
      )}
    </div>
  )
}

export default MoneyFlowRing
