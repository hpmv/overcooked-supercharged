using System;

namespace System.Collections.Generic {
    // Token: 0x0200044A RID: 1098
    [Serializable]
    public class FastList<T> {
        // Token: 0x060013A5 RID: 5029 RVA: 0x0000E38C File Offset: 0x0000C58C
        public FastList() {
            this._items = FastList<T>._emptyArray;
        }

        // Token: 0x060013A6 RID: 5030 RVA: 0x0000E39F File Offset: 0x0000C59F
        public FastList(int capacity) {
            if (capacity < 0) {
                capacity = 0;
            }
            if (capacity == 0) {
                this._items = FastList<T>._emptyArray;
            } else {
                this._items = new T[capacity];
            }
        }

        // Token: 0x060013A7 RID: 5031 RVA: 0x00085AA4 File Offset: 0x00083CA4
        public FastList(IEnumerable<T> collection) {
            ICollection<T> collection2 = null;
            if (collection != null) {
                collection2 = (collection as ICollection<T>);
            }
            if (collection2 != null) {
                int count = collection2.Count;
                if (count == 0) {
                    this._items = FastList<T>._emptyArray;
                } else {
                    this._items = new T[count];
                    collection2.CopyTo(this._items, 0);
                    this._size = count;
                }
            } else {
                this._size = 0;
                this._items = FastList<T>._emptyArray;
                if (collection != null) {
                    foreach (T item in collection) {
                        this.Add(item);
                    }
                }
            }
        }

        // Token: 0x1700026D RID: 621
        // (get) Token: 0x060013A8 RID: 5032 RVA: 0x0000E3D3 File Offset: 0x0000C5D3
        // (set) Token: 0x060013A9 RID: 5033 RVA: 0x00085B6C File Offset: 0x00083D6C
        public int Capacity {
            get {
                return this._items.Length;
            }
            set {
                if (value < this._size) {
                    value = this._size;
                }
                if (value != this._items.Length) {
                    if (value > 0) {
                        T[] array = new T[value];
                        if (this._size > 0) {
                            Array.Copy(this._items, 0, array, 0, this._size);
                        }
                        this._items = array;
                    } else {
                        this._items = FastList<T>._emptyArray;
                    }
                }
            }
        }

        // Token: 0x1700026E RID: 622
        // (get) Token: 0x060013AA RID: 5034 RVA: 0x0000E3DD File Offset: 0x0000C5DD
        public int Count {
            get {
                return this._size;
            }
        }

        // Token: 0x060013AB RID: 5035 RVA: 0x00085BE0 File Offset: 0x00083DE0
        private static bool IsCompatibleObject(object value) {
            return value is T || (value == null && default(T) == null);
        }

        // Token: 0x060013AC RID: 5036 RVA: 0x00085C18 File Offset: 0x00083E18
        public void Add(T item) {
            if (this._size == this._items.Length) {
                this.EnsureCapacity(this._size + 1);
            }
            this._items[this._size++] = item;
        }

        // Token: 0x060013AD RID: 5037 RVA: 0x0000E3E5 File Offset: 0x0000C5E5
        public void AddRange(IEnumerable<T> collection) {
            this.InsertRange(this._size, collection);
        }

        // Token: 0x060013AE RID: 5038 RVA: 0x0000E3F4 File Offset: 0x0000C5F4
        public int BinarySearch(int index, int count, T item, IComparer<T> comparer) {
            if (index < 0) {
                index = 0;
            }
            if (count < 0) {
                count = 0;
            }
            if (this._size - index < count) {
                return -1;
            }
            return Array.BinarySearch<T>(this._items, index, count, item, comparer);
        }

        // Token: 0x060013AF RID: 5039 RVA: 0x0000E42A File Offset: 0x0000C62A
        public int BinarySearch(T item) {
            return this.BinarySearch(0, this.Count, item, null);
        }

        // Token: 0x060013B0 RID: 5040 RVA: 0x0000E43B File Offset: 0x0000C63B
        public int BinarySearch(T item, IComparer<T> comparer) {
            return this.BinarySearch(0, this.Count, item, comparer);
        }

        // Token: 0x060013B1 RID: 5041 RVA: 0x0000E44C File Offset: 0x0000C64C
        public void Clear() {
            if (this._size > 0) {
                Array.Clear(this._items, 0, this._size);
                this._size = 0;
            }
        }

        // Token: 0x060013B2 RID: 5042 RVA: 0x0000E473 File Offset: 0x0000C673
        public bool Contains(T item) {
            return this._size != 0 && this.IndexOf(item) != -1;
        }

        // Token: 0x060013B3 RID: 5043 RVA: 0x0000E490 File Offset: 0x0000C690
        public void CopyTo(T[] array) {
            this.CopyTo(array, 0);
        }

        // Token: 0x060013B4 RID: 5044 RVA: 0x0000E49A File Offset: 0x0000C69A
        public void CopyTo(int index, T[] array, int arrayIndex, int count) {
            if (this._size - index < count) { }
            Array.Copy(this._items, index, array, arrayIndex, count);
        }

        // Token: 0x060013B5 RID: 5045 RVA: 0x0000E4BB File Offset: 0x0000C6BB
        public void CopyTo(T[] array, int arrayIndex) {
            Array.Copy(this._items, 0, array, arrayIndex, this._size);
        }

        // Token: 0x060013B6 RID: 5046 RVA: 0x00085C64 File Offset: 0x00083E64
        private void EnsureCapacity(int min) {
            if (this._items.Length < min) {
                int num = (this._items.Length != 0) ? (this._items.Length * 2) : 4;
                if (num < min) {
                    num = min;
                }
                this.Capacity = num;
            }
        }

        // Token: 0x060013B7 RID: 5047 RVA: 0x0000E4D1 File Offset: 0x0000C6D1
        public bool Exists(Predicate<T> match) {
            return this.FindIndex(match) != -1;
        }

        // Token: 0x060013B8 RID: 5048 RVA: 0x00085CB0 File Offset: 0x00083EB0
        public T Find(Predicate<T> match) {
            if (match == null) { }
            for (int i = 0; i < this._size; i++) {
                if (match(this._items[i])) {
                    return this._items[i];
                }
            }
            return default(T);
        }

        // Token: 0x060013B9 RID: 5049 RVA: 0x00085D08 File Offset: 0x00083F08
        public FastList<T> FindAll(Predicate<T> match) {
            if (match == null) { }
            FastList<T> fastList = new FastList<T>();
            for (int i = 0; i < this._size; i++) {
                if (match(this._items[i])) {
                    fastList.Add(this._items[i]);
                }
            }
            return fastList;
        }

        // Token: 0x060013BA RID: 5050 RVA: 0x0000E4E0 File Offset: 0x0000C6E0
        public int FindIndex(Predicate<T> match) {
            return this.FindIndex(0, this._size, match);
        }

        // Token: 0x060013BB RID: 5051 RVA: 0x0000E4F0 File Offset: 0x0000C6F0
        public int FindIndex(int startIndex, Predicate<T> match) {
            return this.FindIndex(startIndex, this._size - startIndex, match);
        }

        // Token: 0x060013BC RID: 5052 RVA: 0x00085D64 File Offset: 0x00083F64
        public int FindIndex(int startIndex, int count, Predicate<T> match) {
            if (startIndex > this._size) { }
            if (count < 0 || startIndex > this._size - count) { }
            if (match == null) { }
            int num = startIndex + count;
            for (int i = startIndex; i < num; i++) {
                if (match(this._items[i])) {
                    return i;
                }
            }
            return -1;
        }

        // Token: 0x060013BD RID: 5053 RVA: 0x00085DC8 File Offset: 0x00083FC8
        public T FindLast(Predicate<T> match) {
            if (match == null) { }
            for (int i = this._size - 1; i >= 0; i--) {
                if (match(this._items[i])) {
                    return this._items[i];
                }
            }
            return default(T);
        }

        // Token: 0x060013BE RID: 5054 RVA: 0x0000E502 File Offset: 0x0000C702
        public int FindLastIndex(Predicate<T> match) {
            return this.FindLastIndex(this._size - 1, this._size, match);
        }

        // Token: 0x060013BF RID: 5055 RVA: 0x0000E519 File Offset: 0x0000C719
        public int FindLastIndex(int startIndex, Predicate<T> match) {
            return this.FindLastIndex(startIndex, startIndex + 1, match);
        }

        // Token: 0x060013C0 RID: 5056 RVA: 0x00085E24 File Offset: 0x00084024
        public int FindLastIndex(int startIndex, int count, Predicate<T> match) {
            if (match == null) { }
            if (this._size == 0) {
                if (startIndex != -1) { }
            } else if (startIndex >= this._size) { }
            if (count < 0 || startIndex - count + 1 < 0) { }
            int num = startIndex - count;
            for (int i = startIndex; i > num; i--) {
                if (match(this._items[i])) {
                    return i;
                }
            }
            return -1;
        }

        // Token: 0x060013C1 RID: 5057 RVA: 0x00085E9C File Offset: 0x0008409C
        public void ForEach(Action<T> action) {
            if (action == null) { }
            for (int i = 0; i < this._size; i++) {
                action(this._items[i]);
            }
        }

        // Token: 0x060013C2 RID: 5058 RVA: 0x00085ED8 File Offset: 0x000840D8
        public FastList<T> GetRange(int index, int count) {
            if (index < 0) { }
            if (count < 0) { }
            if (this._size - index < count) { }
            FastList<T> fastList = new FastList<T>(count);
            Array.Copy(this._items, index, fastList._items, 0, count);
            fastList._size = count;
            return fastList;
        }

        // Token: 0x060013C3 RID: 5059 RVA: 0x0000E526 File Offset: 0x0000C726
        public int IndexOf(T item) {
            return Array.IndexOf<T>(this._items, item, 0, this._size);
        }

        // Token: 0x060013C4 RID: 5060 RVA: 0x0000E53B File Offset: 0x0000C73B
        public int IndexOf(T item, int index) {
            if (index > this._size) { }
            return Array.IndexOf<T>(this._items, item, index, this._size - index);
        }

        // Token: 0x060013C5 RID: 5061 RVA: 0x0000E55E File Offset: 0x0000C75E
        public int IndexOf(T item, int index, int count) {
            if (index > this._size) { }
            return Array.IndexOf<T>(this._items, item, index, count);
        }

        // Token: 0x060013C6 RID: 5062 RVA: 0x00085F24 File Offset: 0x00084124
        public void Insert(int index, T item) {
            if (index > this._size) { }
            if (this._size == this._items.Length) {
                this.EnsureCapacity(this._size + 1);
            }
            if (index < this._size) {
                Array.Copy(this._items, index, this._items, index + 1, this._size - index);
            }
            this._items[index] = item;
            this._size++;
        }

        // Token: 0x060013C7 RID: 5063 RVA: 0x00085FA4 File Offset: 0x000841A4
        public void InsertRange(int index, IEnumerable<T> collection) {
            if (collection == null) { }
            if (index > this._size) { }
            ICollection<T> collection2 = collection as ICollection<T>;
            if (collection2 != null) {
                int count = collection2.Count;
                if (count > 0) {
                    this.EnsureCapacity(this._size + count);
                    if (index < this._size) {
                        Array.Copy(this._items, index, this._items, index + count, this._size - index);
                    }
                    if (this == collection2) {
                        Array.Copy(this._items, 0, this._items, index, index);
                        Array.Copy(this._items, index + count, this._items, index * 2, this._size - index);
                    } else {
                        collection2.CopyTo(this._items, index);
                    }
                    this._size += count;
                }
            } else {
                foreach (T item in collection) {
                    this.Insert(index++, item);
                }
            }
        }

        // Token: 0x060013C8 RID: 5064 RVA: 0x0000E57A File Offset: 0x0000C77A
        public int LastIndexOf(T item) {
            if (this._size == 0) {
                return -1;
            }
            return this.LastIndexOf(item, this._size - 1, this._size);
        }

        // Token: 0x060013C9 RID: 5065 RVA: 0x0000E59E File Offset: 0x0000C79E
        public int LastIndexOf(T item, int index) {
            if (index >= this._size) { }
            return this.LastIndexOf(item, index, index + 1);
        }

        // Token: 0x060013CA RID: 5066 RVA: 0x000860C0 File Offset: 0x000842C0
        public int LastIndexOf(T item, int index, int count) {
            if (this.Count == 0 || index < 0) { }
            if (this.Count == 0 || count < 0) { }
            if (this._size == 0) {
                return -1;
            }
            if (index >= this._size) { }
            if (count > index + 1) { }
            return Array.LastIndexOf<T>(this._items, item, index, count);
        }

        // Token: 0x060013CB RID: 5067 RVA: 0x00086124 File Offset: 0x00084324
        public bool Remove(T item) {
            int num = this.IndexOf(item);
            if (num >= 0) {
                this.RemoveAt(num);
                return true;
            }
            return false;
        }

        // Token: 0x060013CC RID: 5068 RVA: 0x0008614C File Offset: 0x0008434C
        public int RemoveAll(Predicate<T> match) {
            if (match == null) { }
            int num = 0;
            while (num < this._size && !match(this._items[num])) {
                num++;
            }
            if (num >= this._size) {
                return 0;
            }
            int i = num + 1;
            while (i < this._size) {
                while (i < this._size && match(this._items[i])) {
                    i++;
                }
                if (i < this._size) {
                    this._items[num++] = this._items[i++];
                }
            }
            Array.Clear(this._items, num, this._size - num);
            int result = this._size - num;
            this._size = num;
            return result;
        }

        // Token: 0x060013CD RID: 5069 RVA: 0x00086230 File Offset: 0x00084430
        public void CyclicRemoveAt(int index) {
            this._items[index] = this._items[this._size--];
        }

        // Token: 0x060013CE RID: 5070 RVA: 0x00086268 File Offset: 0x00084468
        public void RemoveAt(int index) {
            if (index >= this._size) { }
            this._size--;
            if (index < this._size) {
                Array.Copy(this._items, index + 1, this._items, index, this._size - index);
            }
            this._items[this._size] = default(T);
        }

        // Token: 0x060013CF RID: 5071 RVA: 0x000862D4 File Offset: 0x000844D4
        public void RemoveRange(int index, int count) {
            if (index < 0) { }
            if (count < 0) { }
            if (this._size - index < count) { }
            if (count > 0) {
                this._size -= count;
                if (index < this._size) {
                    Array.Copy(this._items, index + count, this._items, index, this._size - index);
                }
                Array.Clear(this._items, this._size, count);
            }
        }

        // Token: 0x060013D0 RID: 5072 RVA: 0x0000E5B7 File Offset: 0x0000C7B7
        public void Reverse() {
            this.Reverse(0, this.Count);
        }

        // Token: 0x060013D1 RID: 5073 RVA: 0x0000E5C6 File Offset: 0x0000C7C6
        public void Reverse(int index, int count) {
            if (index < 0) { }
            if (count < 0) { }
            if (this._size - index < count) { }
            Array.Reverse(this._items, index, count);
        }

        // Token: 0x060013D2 RID: 5074 RVA: 0x0000E5F1 File Offset: 0x0000C7F1
        public void Sort() {
            this.Sort(0, this.Count, null);
        }

        // Token: 0x060013D3 RID: 5075 RVA: 0x0000E601 File Offset: 0x0000C801
        public void Sort(IComparer<T> comparer) {
            this.Sort(0, this.Count, comparer);
        }

        // Token: 0x060013D4 RID: 5076 RVA: 0x0000E611 File Offset: 0x0000C811
        public void Sort(int index, int count, IComparer<T> comparer) {
            if (index < 0) { }
            if (count < 0) { }
            if (this._size - index < count) { }
            Array.Sort<T>(this._items, index, count, comparer);
        }

        // Token: 0x060013D5 RID: 5077 RVA: 0x00086350 File Offset: 0x00084550
        public T[] ToArray() {
            T[] array = new T[this._size];
            Array.Copy(this._items, 0, array, 0, this._size);
            return array;
        }

        // Token: 0x060013D6 RID: 5078 RVA: 0x00086380 File Offset: 0x00084580
        public void TrimExcess() {
            int num = (int) ((double) this._items.Length * 0.9);
            if (this._size < num) {
                this.Capacity = this._size;
            }
        }

        // Token: 0x060013D7 RID: 5079 RVA: 0x000863BC File Offset: 0x000845BC
        public bool TrueForAll(Predicate<T> match) {
            if (match == null) { }
            for (int i = 0; i < this._size; i++) {
                if (!match(this._items[i])) {
                    return false;
                }
            }
            return true;
        }

        // Token: 0x04001024 RID: 4132
        private const int _defaultCapacity = 4;

        // Token: 0x04001025 RID: 4133
        public T[] _items;

        // Token: 0x04001026 RID: 4134
        private int _size;

        // Token: 0x04001027 RID: 4135
        [NonSerialized]
        private object _syncRoot;

        // Token: 0x04001028 RID: 4136
        private static readonly T[] _emptyArray = new T[0];
    }
}