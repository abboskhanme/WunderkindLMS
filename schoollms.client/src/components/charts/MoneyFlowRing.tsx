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
  CatmullRomCurve3,
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
const COLOR_INCOME = 0x4da3ff
const COLOR_EXPENSE = 0xffa726
const COLOR_HUB = 0xffd166
const COLOR_PROFIT = 0xffe08a
const COLOR_RING = 0x1e3a8a

/** Disk (galaktika) tashqi radiusi. */
const DISC_R = 6.2
/** Yadro radiusi — markazdagi yorqin oltin to'plam. */
const CORE_R = 0.5
/** Tugunlar joylashadigan radius oralig'i. */
const NODE_R_INNER = 2.6
const NODE_R_OUTER = 5.4
/** Spiral qo'llar soni va burilish kuchi. */
const SPIRAL_ARMS = 4
const SPIRAL_TWIST = 2.3
/** Oqim yo'li yadroga borguncha necha radian buriladi. */
const SPIRAL_SWEEP = 1.9
/** Har bir qo'ldagi nuqta soni. */
const ARM_DOTS = 9000
/** Radius bo'yicha bandlar — differensial aylanish uchun. */
const BANDS = 5


/**
 * Zarralarning umumiy chegarasi — sahna qancha katta bo'lmasin, shundan oshmaydi.
 * Mijoz talabi: yirik nuqtalar emas, MAYDA va KO'P — pul oqimi tuyulsin.
 * 2400 ta zarra bitta `Points` obyektida chiziladi, ya'ni draw-call soni o'zgarmaydi.
 */
const PARTICLE_BUDGET = 3600

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
/** Halqadagi nuqta (XZ tekisligi, y = 0). */
/** Ikki nuqta orasidagi egri naycha o'qi. `lift` — egilish balandligi. */
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
      // Diskka qiyalik bilan qaraymiz — u ellips bo'lib ko'rinadi, ya'ni
      // "yuqoridan biroz burchak ostida" (havoladagi rasmdagi kabi).
      camera.position.set(0, 7.6, 8.6)
      camera.lookAt(0, 0, 0)

      /** Aylanadigan guruh — kamera qimirlamaydi, sahna aylanadi. */
      const spinner = new Group()
      scene.add(spinner)

      // ---- GALAKTIKA: markazda yadro, atrofida spiral qo'llar, tugunlar orbitada ----
      const nodeById = new Map<string, MoneyFlowNode>(flow.nodes.map((n) => [n.id, n]))
      const incomingLinks = flow.links.filter((l) => l.target === 'hub')
      const outgoingLinks = flow.links.filter((l) => l.source === 'hub')
      const leafLinks = [...incomingLinks, ...outgoingLinks]

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
        return incomingLinks.some((l) => l.source === node.id) ? COLOR_EXPENSE : COLOR_PROFIT
      }

      // Tugunlar disk tekisligida, yadro atrofida teng taqsimlanadi.
      // Radius — summaga teskari: katta oqim yadroga yaqinroq, ya'ni "og'irroq".
      const positions = new Map<string, Vector3>()
      positions.set('hub', new Vector3(0, 0, 0))
      leafLinks.forEach((link, i) => {
        const id = link.target === 'hub' ? link.source : link.target
        const angle = (i / Math.max(leafLinks.length, 1)) * Math.PI * 2 + 0.35
        const r = NODE_R_INNER + (NODE_R_OUTER - NODE_R_INNER)
          * (1 - scaleShare(Math.abs(link.value), maxLink))
        positions.set(id, new Vector3(Math.cos(angle) * r, 0, Math.sin(angle) * r))
      })

      // Statik nuqtalar radius bo'yicha BANDlarga bo'linadi — har band o'z
      // tezligi bilan aylanadi (differensial aylanish: ichkarisi tezroq,
      // haqiqiy galaktikadagi kabi). Har band — bitta draw-call.
      const bandPos: number[][] = Array.from({ length: BANDS }, () => [])
      const bandCol: number[][] = Array.from({ length: BANDS }, () => [])

      const pushDot = (x: number, y: number, z: number, c: Color, tint = 1) => {
        const r = Math.hypot(x, z)
        const b = Math.min(BANDS - 1, Math.floor((r / DISC_R) * BANDS))
        bandPos[b].push(x, y, z)
        bandCol[b].push(c.r * tint, c.g * tint, c.b * tint)
      }

      /** Logarifmik spiral: radiusdan burchak. Galaktika qo'lining shakli. */
      const spiralAngle = (r: number, arm: number) =>
        (arm / SPIRAL_ARMS) * Math.PI * 2 + SPIRAL_TWIST * Math.log(r / CORE_R)

      // 1) YADRO — zich, yorqin, oltin. Markazdagi "Umumiy aylanma".
      const coreColor = new Color(COLOR_HUB)
      const coreWhite = new Color(0xfff6e0)
      for (let i = 0; i < 9000; i += 1) {
        const u = Math.pow(Math.random(), 2.2)
        const r = CORE_R * 2.6 * u
        const a = Math.random() * Math.PI * 2
        const y = (Math.random() - 0.5) * CORE_R * 1.1 * (1 - u)
        pushDot(
          Math.cos(a) * r, y, Math.sin(a) * r,
          u < 0.25 ? coreWhite : coreColor,
          0.5 + (1 - u) * 0.9,
        )
      }

      // 2) SPIRAL QO'LLAR — galaktika tanasi. Ko'k va oltin nuqtalar aralash.
      const armBlue = new Color(0x3b82f6)
      const armCyan = new Color(0x7dd3fc)
      const armGold = new Color(0xfbbf24)
      for (let arm = 0; arm < SPIRAL_ARMS; arm += 1) {
        for (let i = 0; i < ARM_DOTS; i += 1) {
          const t = Math.pow(Math.random(), 0.62)
          const r = CORE_R * 1.6 + t * (DISC_R - CORE_R * 1.6)
          const a = spiralAngle(r, arm)
          // Qo'l qalinligi radius bilan o'sadi — chekkasi tarqoq.
          const spread = 0.22 + 0.55 * t
          const g = () => (Math.random() + Math.random() + Math.random() - 1.5) * spread
          const roll = Math.random()
          const c = roll < 0.62 ? armBlue : roll < 0.86 ? armCyan : armGold
          pushDot(
            Math.cos(a) * r + g(),
            (Math.random() - 0.5) * (0.12 + 0.22 * t),
            Math.sin(a) * r + g(),
            c,
            0.25 + Math.random() * 0.75,
          )
        }
      }

      // 3) UZOQ YULDUZLAR — disk tekisligidan tashqarida, fon.
      const starColor = new Color(0xbcd4ff)
      for (let i = 0; i < 2200; i += 1) {
        const r = DISC_R * (1.05 + Math.random() * 1.5)
        const a = Math.random() * Math.PI * 2
        pushDot(
          Math.cos(a) * r,
          (Math.random() - 0.5) * DISC_R * 0.7,
          Math.sin(a) * r,
          starColor,
          0.12 + Math.random() * 0.35,
        )
      }

      // ---- Tugunlar: yorqin to'plam + orbita + ko'rinmas tutqich + yorliq ----
      const pickable: Mesh[] = []

      for (const node of flow.nodes) {
        const pos = positions.get(node.id)
        if (!pos || node.kind === 'hub') continue

        const color = colorOf(node)
        const c = new Color(color)
        const radius = 0.16 + 0.3 * scaleShare(node.value, maxLink)
        const orbitR = Math.hypot(pos.x, pos.z)

        // Orbita — ingichka nuqtali ellips (disk tekisligida).
        const orbitColor = new Color(COLOR_RING)
        const orbitDots = Math.round(260 + orbitR * 60)
        for (let i = 0; i < orbitDots; i += 1) {
          const a = (i / orbitDots) * Math.PI * 2
          pushDot(
            Math.cos(a) * orbitR, (Math.random() - 0.5) * 0.02, Math.sin(a) * orbitR,
            orbitColor, 0.3 + Math.random() * 0.35,
          )
        }

        // Tugun to'plami — yorqin yadro + xira gало.
        const dots = Math.round(600 + 1500 * scaleShare(node.value, maxLink))
        for (let i = 0; i < dots; i += 1) {
          const u = Math.pow(Math.random(), 1.8)
          const r = radius * 2.1 * u
          const theta = Math.random() * Math.PI * 2
          const phi = Math.acos(2 * Math.random() - 1)
          pushDot(
            pos.x + r * Math.sin(phi) * Math.cos(theta),
            pos.y + r * Math.cos(phi),
            pos.z + r * Math.sin(phi) * Math.sin(theta),
            c,
            0.4 + (1 - u) * 1.0,
          )
        }

        const picker = new Mesh(
          track(new SphereGeometry(radius * 2.2, 16, 12)),
          track(new MeshBasicMaterial({ transparent: true, opacity: 0, depthWrite: false })),
        )
        picker.position.copy(pos)
        picker.userData = {
          hover: { label: node.label, value: node.value, kind: node.kind } satisfies HoverInfo,
          baseColor: color,
        }
        spinner.add(picker)
        pickable.push(picker)

        const label = makeLabelSprite(node.label, `${shortSom(node.value)} so'm`)
        label.position.copy(pos.clone().setY(radius * 2.2 + 0.55))
        spinner.add(label)
        if (label.material.map) disposables.push(label.material.map)
        disposables.push(label.material)
      }

      // Markaziy yorliq va tutqich.
      {
        const hub = nodeById.get('hub')
        if (hub) {
          const picker = new Mesh(
            track(new SphereGeometry(CORE_R * 2.2, 16, 12)),
            track(new MeshBasicMaterial({ transparent: true, opacity: 0, depthWrite: false })),
          )
          picker.userData = {
            hover: { label: hub.label, value: hub.value, kind: 'hub' } satisfies HoverInfo,
            baseColor: COLOR_HUB,
          }
          spinner.add(picker)
          pickable.push(picker)

          const label = makeLabelSprite(hub.label, `${shortSom(hub.value)} so'm`)
          label.position.set(0, CORE_R * 2.4 + 0.5, 0)
          label.scale.set(LABEL_SCALE * 1.35, (LABEL_SCALE * 1.35) / LABEL_ASPECT, 1)
          spinner.add(label)
          if (label.material.map) disposables.push(label.material.map)
          disposables.push(label.material)
        }
      }

      // ---- Oqim yo'llari: tugundan yadroga (yoki teskari) spiral bo'ylab ----
      const curves: CatmullRomCurve3[] = []
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
        const c = new Color(color)
        const outer = incoming ? from : to

        // Tugundan yadroga spiral: radius kamayadi, burchak buriladi.
        const r0 = Math.hypot(outer.x, outer.z)
        const a0 = Math.atan2(outer.z, outer.x)
        const samples: Vector3[] = []
        const STEPS = 26
        for (let i = 0; i <= STEPS; i += 1) {
          const t = i / STEPS
          const r = r0 * (1 - t) + CORE_R * 0.55 * t
          const a = a0 + t * SPIRAL_SWEEP * (incoming ? 1 : -1)
          samples.push(new Vector3(Math.cos(a) * r, Math.sin(t * Math.PI) * 0.10, Math.sin(a) * r))
        }
        const curve = new CatmullRomCurve3(incoming ? samples : samples.reverse())
        curve.curveType = 'centripetal'

        // Yo'l bo'ylab chang — oqim ko'rinadigan bo'lsin.
        const spread = 0.05 + 0.14 * scaleShare(link.value, maxLink)
        const dust = Math.round(900 + 2600 * scaleShare(link.value, maxLink))
        const tmp = new Vector3()
        for (let i = 0; i < dust; i += 1) {
          const t = Math.random()
          curve.getPoint(t, tmp)
          const rad = spread * Math.sqrt(Math.random())
          const a = Math.random() * Math.PI * 2
          pushDot(
            tmp.x + Math.cos(a) * rad,
            tmp.y + (Math.random() - 0.5) * rad,
            tmp.z + Math.sin(a) * rad,
            c,
            0.25 + Math.random() * 0.5,
          )
        }

        const picker = new Mesh(
          track(new TubeGeometry(curve, 40, spread + 0.06, 8, false)),
          track(new MeshBasicMaterial({ transparent: true, opacity: 0, depthWrite: false })),
        )
        picker.userData = {
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
        spinner.add(picker)
        pickable.push(picker)

        curves.push(curve)
        linkShares.push(Math.abs(link.value) / totalAbs)
        linkColors.push(c)
      }

      // ---- Statik bandlar sahnaga (differensial aylanish uchun alohida) ----
      const bands: Points[] = []
      for (let b = 0; b < BANDS; b += 1) {
        if (bandPos[b].length === 0) continue
        const g = track(new BufferGeometry())
        g.setAttribute('position', new BufferAttribute(new Float32Array(bandPos[b]), 3))
        g.setAttribute('color', new BufferAttribute(new Float32Array(bandCol[b]), 3))
        const pts = new Points(
          g,
          track(
            new PointsMaterial({
              size: 0.028,
              map: track(makeDotTexture()),
              vertexColors: true,
              transparent: true,
              depthWrite: false,
              blending: AdditiveBlending,
              sizeAttenuation: true,
            }),
          ),
        )
        pts.userData.bandSpeed = SPIN_RAD_PER_SEC * (1.9 - (b / BANDS) * 1.35)
        spinner.add(pts)
        bands.push(pts)
      }

      // ---- Harakatlanuvchi zarralar (yo'llar bo'ylab oqim) ----
      // Zichlik VA tezlik summaga proporsional: katta oqim — ko'p va tez
      // nuqtalar. Bular statik changdan sal yorqinroq, shunda harakat ko'zga
      // tashlanadi. Hammasi bitta `Points` — qo'shimcha draw-call yo'q.
      let particles: Points | null = null
      let particleCurve: Int32Array = new Int32Array(0)
      let particleT = new Float32Array(0)
      let particleSpeed = new Float32Array(0)

      if (curves.length > 0) {
        const counts = linkShares.map((share) =>
          Math.max(60, Math.round(PARTICLE_BUDGET * share)),
        )
        const total = counts.reduce((a, b) => a + b, 0)

        const pos = new Float32Array(total * 3)
        const col = new Float32Array(total * 3)
        particleCurve = new Int32Array(total)
        particleT = new Float32Array(total)
        particleSpeed = new Float32Array(total)

        let cursor = 0
        const point = new Vector3()
        counts.forEach((count, linkIndex) => {
          const share = linkShares[linkIndex]
          const speed = 0.05 + 0.16 * Math.sqrt(share)
          const c = linkColors[linkIndex]
          for (let i = 0; i < count; i += 1) {
            particleCurve[cursor] = linkIndex
            particleT[cursor] = (i / count + Math.random() * 0.004) % 1
            particleSpeed[cursor] = speed * (0.85 + Math.random() * 0.3)
            curves[linkIndex].getPoint(particleT[cursor], point)
            pos[cursor * 3] = point.x
            pos[cursor * 3 + 1] = point.y
            pos[cursor * 3 + 2] = point.z
            const tint = 0.9 + Math.random() * 0.5
            col[cursor * 3] = c.r * tint
            col[cursor * 3 + 1] = c.g * tint
            col[cursor * 3 + 2] = c.b * tint
            cursor += 1
          }
        })

        const geometry = track(new BufferGeometry())
        geometry.setAttribute('position', new BufferAttribute(pos, 3))
        geometry.setAttribute('color', new BufferAttribute(col, 3))
        particles = new Points(
          geometry,
          track(
            new PointsMaterial({
              size: 0.042,
              map: track(makeDotTexture()),
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

          // Differensial aylanish: ichki bandlar tezroq — haqiqiy galaktikadagi kabi.
          for (const band of bands) {
            band.rotation.y += (band.userData.bandSpeed as number) * delta
          }
          spinner.rotation.y += SPIN_RAD_PER_SEC * 0.35 * delta

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
