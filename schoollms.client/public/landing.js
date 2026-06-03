// Intellect School — landing interaktivligi (tashqi fayl: prod CSP script-src 'self' bilan ishlaydi).

// Nav soyasi (skroll qilinganda)
const nav = document.getElementById('nav');
const onScroll = () => nav.classList.toggle('scrolled', window.scrollY > 8);
window.addEventListener('scroll', onScroll, { passive: true }); onScroll();

// FAQ akkordeon
document.querySelectorAll('.faq-q').forEach((q) => {
  q.addEventListener('click', () => {
    const item = q.closest('.faq-item');
    const a = item.querySelector('.faq-a');
    const open = item.classList.contains('open');
    document.querySelectorAll('.faq-item.open').forEach((o) => {
      o.classList.remove('open');
      o.querySelector('.faq-a').style.maxHeight = null;
    });
    if (!open) { item.classList.add('open'); a.style.maxHeight = a.scrollHeight + 'px'; }
  });
});

// Mobil menyu — burger nav-links'ni ochib/yopadi (avval faqat #narxlar'ga sakrardi)
const burger = document.getElementById('burger');
if (burger) burger.addEventListener('click', () => nav.classList.toggle('menu-open'));
document.querySelectorAll('.nav-links a').forEach((a) =>
  a.addEventListener('click', () => nav.classList.remove('menu-open')));
