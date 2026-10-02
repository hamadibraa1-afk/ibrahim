/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./src/**/*.{html,ts}"],
  theme: {
    extend: {
      colors: {
        // مستخلصة من شعار جمعية الشارقة الخيرية
        brand: {
          50:  '#F0F7F3',
          100: '#DCEDE3',
          200: '#B9DBC8',
          300: '#8CC3A5',
          400: '#5CA57E',
          500: '#3B7E49',   // أخضر الشعار
          600: '#2D6339',
          700: '#1B4332',   // أخضر داكن
          800: '#143329',
          900: '#0D2119',
        },
        sun: {
          50:  '#FDF5EE',
          100: '#FAE8D7',
          200: '#F3CFAD',
          300: '#EBB07E',
          400: '#DB9357',   // برتقالي الشعار
          500: '#C87A3C',
          600: '#A65F2C',
        },
        // نص ثانوي أغمق من السابق ليبقى مقروءاً فوق الزجاج (تباين ≥ 4.5:1)
        ink: {
          DEFAULT: '#1F2937',
          soft: '#4B5563',
          faint: '#4F5664',
        },
        // ألوان تستخدمها شاشة إدارة المستخدمين ولم تكن معرّفة (كان زر الإضافة غير ظاهر)
        navy: '#1B4332',
        teal: {
          DEFAULT: '#1B4332',
          light: '#2D6339',
          soft: '#DCEDE3',
        },
      },
      fontFamily: {
        sans: ['"Noto Sans Arabic"', '"Segoe UI"', 'Tahoma', 'sans-serif'],
      },
      boxShadow: {
        card: '0 1px 2px rgba(16,24,40,0.04), 0 1px 3px rgba(16,24,40,0.06)',
        lift: '0 4px 16px rgba(27,67,50,0.10)',
      },
    },
  },
  plugins: [],
}
