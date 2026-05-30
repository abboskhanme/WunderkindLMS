import type { Student } from '@/types'

const E = '2026-01-01' // demo qabul sanasi
const D = { discountPct: 0, discountAmount: 0, discountNote: '' }

export const studentsMock: Student[] = [
  { id: 's1', fullName: 'Aziz Karimov', birthDate: '2010-02-14', address: 'Toshkent, Chilonzor 12', gender: 'male', parentFullName: 'Karimov Rustam', parentPhone: '+998 90 111 22 33', className: '9-A', enrollmentDate: E, balance: -850000, ...D },
  { id: 's2', fullName: 'Malika Yusupova', birthDate: '2010-06-21', address: 'Toshkent, Yunusobod 4', gender: 'female', parentFullName: 'Yusupov Aziz', parentPhone: '+998 91 222 33 44', className: '9-A', enrollmentDate: E, balance: 0, ...D },
  { id: 's3', fullName: 'Sherzod Rahimov', birthDate: '2011-09-03', address: 'Toshkent, Mirzo Ulug\'bek 7', gender: 'male', parentFullName: 'Rahimova Nodira', parentPhone: '+998 93 333 44 55', className: '8-B', enrollmentDate: E, balance: -500000, ...D },
  { id: 's4', fullName: 'Gulnoza Toshmatova', birthDate: '2012-11-17', address: 'Toshkent, Sergeli 9', gender: 'female', parentFullName: 'Toshmatov Bobur', parentPhone: '+998 94 444 55 66', className: '7-A', enrollmentDate: E, balance: 0, ...D },
  { id: 's5', fullName: 'Jahongir Aliyev', birthDate: '2013-03-29', address: 'Toshkent, Olmazor 2', gender: 'male', parentFullName: 'Aliyeva Sevara', parentPhone: '+998 95 555 66 77', className: '6-A', enrollmentDate: E, balance: -900000, ...D },
  { id: 's6', fullName: 'Dilnoza Islomova', birthDate: '2014-05-12', address: 'Toshkent, Yashnobod 15', gender: 'female', parentFullName: 'Islomov Davron', parentPhone: '+998 97 666 77 88', className: '5-A', enrollmentDate: E, balance: 0, ...D },
  { id: 's7', fullName: 'Otabek Soliyev', birthDate: '2014-08-08', address: 'Toshkent, Bektemir 3', gender: 'male', parentFullName: 'Soliyev Farrux', parentPhone: '+998 99 777 88 99', className: '5-A', enrollmentDate: E, balance: 425000, ...D },
  { id: 's8', fullName: 'Sevinch Ortiqova', birthDate: '2014-10-25', address: 'Toshkent, Chilonzor 6', gender: 'female', parentFullName: 'Ortiqova Zarina', parentPhone: '+998 90 888 99 00', className: '5-B', enrollmentDate: E, balance: 0, ...D },
  { id: 's9', fullName: 'Bekzod Mirzayev', birthDate: '2010-01-19', address: 'Toshkent, Shayxontohur 8', gender: 'male', parentFullName: 'Mirzayev Ulug\'bek', parentPhone: '+998 91 999 00 11', className: '9-A', enrollmentDate: E, balance: -1100000, ...D },
  { id: 's10', fullName: 'Nodira Hamidova', birthDate: '2011-07-30', address: 'Toshkent, Yunusobod 11', gender: 'female', parentFullName: 'Hamidov Sardor', parentPhone: '+998 93 100 20 30', className: '8-B', enrollmentDate: E, balance: 0, ...D },
  { id: 's11', fullName: 'Akmal Jo\'rayev', birthDate: '2012-12-05', address: 'Toshkent, Mirobod 5', gender: 'male', parentFullName: 'Jo\'rayeva Manzura', parentPhone: '+998 94 200 30 40', className: '7-A', enrollmentDate: E, balance: -950000, ...D },
  { id: 's12', fullName: 'Charos Abdullayeva', birthDate: '2013-04-16', address: 'Toshkent, Sergeli 14', gender: 'female', parentFullName: 'Abdullayev Jasur', parentPhone: '+998 95 300 40 50', className: '6-A', enrollmentDate: E, balance: 0, ...D },
  { id: 's13', fullName: 'Sardor Nazarov', birthDate: '2009-10-22', address: 'Toshkent, Olmazor 18', gender: 'male', parentFullName: 'Nazarova Feruza', parentPhone: '+998 97 400 50 60', className: '10-A', enrollmentDate: E, balance: 600000, ...D },
  { id: 's14', fullName: 'Mohira Qodirova', birthDate: '2008-08-11', address: 'Toshkent, Yashnobod 1', gender: 'female', parentFullName: 'Qodirov Shavkat', parentPhone: '+998 99 500 60 70', className: '11-A', enrollmentDate: E, balance: -1300000, ...D },
]
