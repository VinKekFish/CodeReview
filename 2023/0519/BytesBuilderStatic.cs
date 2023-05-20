using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

using static cryptoprime.BytesBuilderForPointers;

namespace cryptoprime
{
    public unsafe partial class BytesBuilderStatic: IDisposable
    {
        public nint size;
        // allocator никогда не null, см. стр. 27 оригнального файла
        public readonly AllocatorForUnsafeMemoryInterface? allocator = null;

        public const int MIN_SIZE = 2;
        public BytesBuilderStatic(nint Size, AllocatorForUnsafeMemoryInterface? allocator = null)
        {
            if (Size < MIN_SIZE)
                throw new ArgumentOutOfRangeException("BytesBuilderStatic.BytesBuilderStatic: Size < MIN_SIZE");

            // allocator никогда не null
            this.allocator = allocator ?? new AllocHGlobal_AllocatorForUnsafeMemory();

            // Устанаваливаем размер, логично
            // size, при этом, не инициализирован (точнее, инициализирован нулём)
            Resize(Size);
        }

        /// <summary>Изменяет размер циклического буфера без потери данных.<para>При многопоточных вызовах синхронизация остаётся на пользователе.</para></summary>
        /// <param name="Size">Новый размер</param>
        public void Resize(nint Size)
        {
            if (Size < MIN_SIZE)
                throw new ArgumentOutOfRangeException("BytesBuilderStatic.Resize: Size < MIN_SIZE");
            // Если храним больше, чем сейчас пытаемся уменьшить размер - не даём уменьшить размер
            if (Count > Size)
                throw new ArgumentOutOfRangeException("BytesBuilderStatic.Resize: Count > Size");
// newRegion никогда не null; либо Exception
            var newRegion = allocator?.AllocMemory(Size) ?? throw new ArgumentNullException("BytesBuilderStatic.Resize");
// oldRegion никогда не null, исключая случаи, когда region не инициализирован
            var oldRegion = region;

            if (oldRegion != null)
            {
                // Копируем байты из существующего массива в новый
                // Нашли небольшую ошибку: если count == 0, мы не должны ничего копировать
                // Если здесь будет ошибка, то newRegion не будет корректно удалён из памяти
                ReadBytesTo(newRegion.array, count);
                oldRegion.Dispose();
                oldRegion = null;
            }

            region = newRegion;
            size   = Size;
            bytes  = region.array;
            after  = bytes + region.len;
            Start  = 0;
            End    = Count;

            // Есть одна проблема. Когда Count == size, End должен быть равен 0
        }

        /// <summary>Прочитать count байтов из циклического буфера в массив target</summary>
        /// <param name="target">Целевой массив, куда копируются значения</param>
        /// <param name="count">Количество байтов для копирования. Если меньше нуля, то копируются все байты</param>
        public void ReadBytesTo(byte* target, nint count = -1)
        {
            if (count < 0)
                count = Count;

            if (count > Count)
                throw new ArgumentOutOfRangeException("ReadBytesTo: count > Count");

            // Start должен быть всегда в пределах массива region
            var s1 = bytes + Start;
            var l1 = len1;
            var l2 = len2;

            if (count <= 0)
                throw new ArgumentOutOfRangeException("ReadBytesTo: count <= 0");
            // Проверяем, что левая и правая длины верно рассчитаны
            if (l1 + l2 != this.count)
                throw new Exception("ReadBytesTo: Fatal algorithmic error: l1 + l2 != this.count");

            // Копируем часть массива справа
            // Есть одна проблема: l1 может быть равен нулю
            BytesBuilder.CopyTo(l1, count, s1, target);

            // Вычисляем оставшуюся часть запрошенных данных
            var lc = count - l1;
            // Если мы не всё вывели
            // Здесь опять проблема: если lc > 0 и l2 <= 0 - это ошибка
            // Если lc > 0, то и l2 > 0, так как count <= this.Count
            // l1 + l2 = this.count
            // Значит, осталось ещё count - l1
            // Если l1 == count, то lc = count - count, то есть 0
            // Значит, l1 < count
            // Тогда l2 = this.count -l1 > 0
            if (l2 > 0 && lc > 0)
            // target + l1 - очевидно, мы уже скопировали в target l1 байтов
            // так что начинаем с l1 байта
            // копируем, начиная с bytes, так как сейчас мы работаем с левой половиной массива
            BytesBuilder.CopyTo(l2, lc, bytes, target + l1);
        }

        /// <summary>Записывает байты в циклический буфер (добавляет байты в конец)</summary>
        /// <param name="source">Источник, из которого берутся данные</param>
        /// <param name="countToWrite">Количество байтов для добавления</param>
        public void WriteBytes(byte* source, nint countToWrite)
        {
            if (count + countToWrite > size)
                throw new ArgumentOutOfRangeException("WriteBytes: count + countToWrite > size");
            if (countToWrite <= 0)
                throw new ArgumentOutOfRangeException("WriteBytes: countToWrite <= 0");

            if (End >= Start)
            {
                // End >= Start означает, что массив либо полностью полон, либо пуст,
                // либо содержит только правую часть
                // s1, таким образом, указывает на следующий добавляемый байт
                // т.к. count + countToWrite <= size не может быть такого,
                // что массив уже полностью заполнен
                var s1 = bytes + End;
                // after указывает на следующий за последним байтом региона байт
                // Если s1 = bytes + size-1 (End < size)
                // то l1 = bytes + size - s1 = bytes + size - bytes - End = size - End =
                // = size - size + 1 = 1
                var l1 = (nint) (after - s1);

                // Если права половина массива существует, то выполняем копирование
                // Из источника в приёмник
                var A  = l1 > 0 ? BytesBuilder.CopyTo(countToWrite, l1, source, s1) : 0;
                count += A;
                End   += A;

                if (A != l1 && A != countToWrite)
                    throw new Exception("WriteBytes: Fatal algorithmic error: A != l1 && A != countToWrite");

                // Если мы записали всю первую половину
                // То мы должны перейти в начало массива
                if (End >= size)
                {
                    if (End == size)
                        End = 0;
                    else
                        throw new Exception("WriteBytes: Fatal algorithmic error: End > size");
                }

                // Если ещё остались байты для записи
                // Здесь мы сместились в источнике на A записанных байтов.
                // Количество байтов к записи уменьшили на то же значение
                // Количество байтов к записи - никогда не ноль
                if (A < countToWrite)
                    WriteBytes(source + A, countToWrite - A);
            }
            else    // End < Start, запись во вторую половину циклического буфера
            {
                // Это значит, что мы будем записывать в левую половину буфера, т.к. правая уже занята
                // Берём адрес первого байта для записи
                // Например, если мы вызвали рекурсивно WriteBytes с предыдущей ветки
                // то End == 0, то есть s1 указывает на начало массива
                var s1 = bytes + End;
                // Здесь мы вычисляем адрес элемента, идущего за последним элементом массива,
                // в который возможна запись
                // Это (bytes + Start)
                // Очевидно, что именно в этот элемент записывать уже нельзя,
                // т.к. Start показывает адрес первого элемента, который уже записан
                // Далее отнимаем от этого элемента s1
                // Таким образом мы получаем возможное количество элементов для записи
                // Например, если s1 будет указывать на последний элемент перед Start,
                // то получится bytes + Start - (bytes + Start - 1) = 1
                var l1 = (nint)(   (bytes + Start) - s1   );

                // Приращаем End и count на количество записанных байтов (элементов массива)
                var A  = BytesBuilder.CopyTo(countToWrite, l1, source, s1);
                count += A;
                End   += A;

                if (A != countToWrite)
                    throw new Exception("WriteBytes: Fatal algorithmic error: A != countToWrite");

                if (End > Start)
                    throw new Exception("WriteBytes: Fatal algorithmic error: End > Start");
            }
        }

                                                                                /// <summary>Адрес циклического буфера == region.array</summary>
        protected byte *  bytes  = null;                                        /// <summary>Поле, указывающее на первый байт после конца массива</summary>
        protected byte *  after  = null;                                        /// <summary>Обёртка для циклического буфера</summary>
        protected Record? region = null;
                                                                                /// <summary>Количество всех сохранённых байтов в этом объекте - сырое поле для корректировки значений</summary>
        protected nint count = 0;                                               /// <summary>Количество всех сохранённых байтов в этом объекте</summary>
        public nint Count => count;
                                                                                /// <summary>End - это индекс следующего добавляемого байта. Для Start = 0 поле End должно быть равно размеру сохранённых данных (End == Count)</summary>
        protected nint Start = 0, End = 0;

        /// <summary>Получает адрес элемента с индексом index</summary>
        /// <param name="index">Индекс получаемого элемента</param>
        /// <returns>Адрес элемента массива</returns>
        public byte * this[nint index]
        {
            get
            {
                if (index >= count)
                    throw new ArgumentOutOfRangeException();

                // Вычисляем адрес первого элемента и смещения относительно него
                var p = bytes + Start + index;

                // Если адрес не уходит дальше выделенного региона памяти,
                // значит он в правой части буфера
                if (p < after)
                {
                    return p;
                }
                // Иначе адрес в левой части буфера
                // При этом, действительно, End <= Start, т.к. все элементы располагаются между Start и End и будут в правой части
                else // End <= Start
                {
                    // Если Start == 0, то len1 = size. Иначе, длина постепенно уменьшается. Всё верно
                    // Если Start == size - 1, то len1 = 1; тоже верно
                    var len1 = size - Start;    // Длина первой (правой) части массива в циклическом буфере
                    // Вычитаем из индекса длину правой части
                    // Теперь в индекс у нас идёт по элементам в левой части
                    index   -= len1;

                    // Вычисляем адрес запрашиваемого элемента
                    p = bytes + index;

                    return p;
                }
            }
        }

        /// <summary>Длина данных, приходящихся на правый (первый) сегмент данных</summary>
        public nint len1
        {
            get
            {
                checked
                {
                    // Если все данные в правом сегменте
                    if (End > Start)
                    {
                        return End - Start;
                    }
                    // Если есть данные и в левом сегменте или весь массив полон
                    else
                    {
                        // Вычисляем размер правого сегмента путём вычитания из левого края (точнее, границы левого края)
                        // начального адреса массива
                        var r = (nint) (after - (bytes + Start));

                        // Если у нас нет данных, то ничего не вычисляем
                        // Перенёс выше вычисления r
                        if (count == 0)
                            return 0;

                        // Возвращаем результат
                        return r;
                    }
                }
            }
        }

        /// <summary>Длина данных, приходящихся на левый сегмент данных</summary>
        public nint len2
        {
            get
            {
                // Если все данные в правом сементе - возвращаем ноль
                if (End > Start || count == 0)
                    return 0;

                // Иначе возвращаем End, т.к. он равен как раз размеру данных в левом сегменте
                return End;
            }
        }

        /// <summary>Добавляет блок в объект</summary><param name="bytesToAdded">Добавляемый блок данных. Содержимое копируется</param><param name="len">Длина добавляемого блока данных</param>
        public void add(byte * bytesToAdded, nint len)
        {
            if (count + len > size)
                throw new IndexOutOfRangeException("BytesBuilderStatic.add: count + len > size: many bytes to add");

            WriteBytes(bytesToAdded, len);
        }

        /// <summary>Добавляет массив в сохранённые значения</summary>
        /// <param name="rec">Добавляемый массив (копируется)</param>
        public void add(Record rec, int len = -1)
        {
            if (len < 0)
                add(rec.array, rec.len);
            else
                add(rec.array, len);
        }

        /// <summary>Очищает циклический буфер</summary>
        /// <param name="fast">fast = <see langword="false"/> - обнуляет выделенный под регион массив памяти</param>
        public void Clear(bool fast = false)
        {
            count = 0;
            Start = 0;
            End   = 0;

            if (!fast)
                BytesBuilder.ToNull(size, bytes);
        }

        /// <summary>Создаёт массив байтов, включающий в себя все сохранённые массивы</summary>
        /// <param name="resultCount">Размер массива-результата (если нужны все байты resultCount = -1)</param>
        /// <param name="resultA">Массив, в который будет записан результат. Если resultA = null, то массив создаётся</param>
        /// <param name="allocator">Аллокатор, который позволяет функции выделять память, если resultA == null. Если null, используется this.allocator</param>
        /// <returns></returns>
        public Record getBytes(nint resultCount = -1, Record? resultA = null, AllocatorForUnsafeMemoryInterface? allocator = null)
        {
            if (resultCount <= -1)
                resultCount = count;

            if (resultCount > count)
            {
                throw new System.ArgumentOutOfRangeException("resultCount", "resultCount is too large: resultCount > count || resultCount == 0");
            }

            if (resultCount == 0)
            {
                throw new System.ArgumentOutOfRangeException("resultCount", "resultCount == 0");
            }

            if (resultA != null && resultA.len < resultCount)
                throw new System.ArgumentOutOfRangeException("resultA", "resultA is too small");
            if (resultA != null && resultA.isDisposed)
                throw new ArgumentOutOfRangeException(nameof(resultA), "BytesBuilderStatic.getBytesAndRemoveIt: resultA.isDisposed");

            var result = resultA ?? allocator?.AllocMemory(resultCount) ?? this?.allocator?.AllocMemory(resultCount) ?? throw new ArgumentNullException("BytesBuilderStatic.getBytes");

            ReadBytesTo(result.array, resultCount);

            return result;
        }

        /// <summary>Удаляет байты из начала массива</summary>
        /// <param name="len">Количество байтов к удалению</param>
        /// <param name="fast">Если false, то удаляемые байты будут перезаписаны</param>
        public void RemoveBytes(nint len, bool fast = true)
        {
            if (len > count)
                throw new ArgumentOutOfRangeException();

            // Обнуление удаляемых байтов
            // Не сказать, что это очень эффективно
            if (!fast)
            for (nint i = 0; i < len; i++)
            {
                *this[i] = 0;
            }

            // Делаем приращение адреса первого элемента - логично
            Start += len;
            // Уменьшаем количество сохранённых элементов на количество удалённых элементов
            count -= len;
            if (Start >= size)
            {
                // Start никогда не будет меньше нуля
                // Если все элементы были в правой части, то сюда мы не войдём,
                // т.к. Start += len у нас, тогда, так и останутся в правой части
                // Значит, элементы были уже в левой части
                // Далее мы пойдём приращать элементы уже начиная с нуля.
                // Здесь как раз мы будем работать по модулю size
                Start -= size;

                if (Start + count != End)
                    throw new Exception("BytesBuilderStatic.RemoveBytes: Fatal algorithmic error: Start + count != End");
            }
        }

        /// <summary>Создаёт массив байтов, включающий в себя count байтов из буфера, и удаляет их с очисткой</summary>
        /// <param name="result">Массив, в который будет записан результат. Уже должен быть выделен. result != <see langword="null"/>.</param>
        /// <param name="count">Длина запрашиваемых данных</param>
        /// <returns></returns>
        public Record getBytesAndRemoveIt(Record result, nint count = -1)
        {
            if (count < 0)
                count = Math.Min(this.count, result.len);

            if (count == 0)
                throw new ArgumentOutOfRangeException(nameof(result), "BytesBuilderStatic.getBytesAndRemoveIt: count == 0");
            if (count > result.len)
                throw new ArgumentOutOfRangeException(nameof(result), "BytesBuilderStatic.getBytesAndRemoveIt: count > result.len");
            if (count > this.count)
                throw new ArgumentOutOfRangeException(nameof(count), "BytesBuilderStatic.getBytesAndRemoveIt: count > this.count");
            if (result.isDisposed)
                throw new ArgumentOutOfRangeException(nameof(result), "BytesBuilderStatic.getBytesAndRemoveIt: result.isDisposed");

            ReadBytesTo(result.array, count);
            RemoveBytes(count);

            return result;
        }

        /// <summary>Очищает и освобождает всю небезопасно выделенную под объект память</summary>
        /// <param name="disposing">Всегда true, кроме вызова из деструктора</param>
        public virtual void Dispose(bool disposing = true)
        {
            if (region == null)
                return;

            region?.Dispose();
            region = null;

            if (!disposing)
                throw new Exception("~BytesBuilderStatic: region != null");
        }
                                                                /// <summary>Очищает и освобождает всю небезопасно выделенную под объект память</summary>
        public void Dispose()
        {
            Dispose(true);
        }
                                                                 /// <summary>Деструктор</summary>
        ~BytesBuilderStatic()
        {
            Dispose(false);
        }
    }
}
