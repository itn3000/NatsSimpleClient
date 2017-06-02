namespace nats_simple_client
{
    using System.Collections.Generic;
    using System.Collections;
    using System.Linq;
    using System;
    struct ValueArraySegment<T> : IEnumerable<T>
    {
        int _offset;
        int _count;
        T[] _array;
        public T[] Array { get { return _array; } }
        public int Count { get { return _count; } }
        public int Offset { get { return _offset; } }
        public ValueArraySegment(T[] ar, int offset, int count)
        {
            _array = ar;
            _offset = offset;
            _count = count;
        }
        public T this[int index]
        {
            get
            {
                return _array[_offset + index];
            }
        }
        public struct Enumerator : IEnumerator<T>
        {
            T[] m_Array;
            int CurrentIndex;
            int EndIndex;
            // int Offset;
            // bool IsEnd;
            public bool MoveNext()
            {
                if (CurrentIndex >= EndIndex - 1)
                {
                    return false;
                }
                else
                {
                    CurrentIndex++;
                    return true;
                }
            }

            public void Reset()
            {
                throw new NotImplementedException();
            }

            public void Dispose()
            {
            }

            public T Current
            {
                get
                {
                    return m_Array[CurrentIndex];
                }
            }

            object IEnumerator.Current => Current;

            internal Enumerator(ref ValueArraySegment<T> ar)
            {
                m_Array = ar.Array;
                CurrentIndex = ar.Offset - 1;
                EndIndex = ar.Offset + ar.Count;
                // IsEnd = false;
                // Offset = ar.Offset;
            }
        }
        public ValueArraySegment<T>.Enumerator GetEnumerator()
        {
            return new ValueArraySegment<T>.Enumerator(ref this);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public int Length
        {
            get
            {
                return _count;
            }
        }
    }
}