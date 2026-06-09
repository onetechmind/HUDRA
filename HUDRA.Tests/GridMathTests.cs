using HUDRA.Services.GamepadInput;
using Xunit;

namespace HUDRA.Tests
{
    public class GridMathTests
    {
        // 2-column grid with 5 items:
        //   0 1
        //   2 3
        //   4
        private const int Count = 5;
        private const int Columns = 2;

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)] // right flows to the next row
        [InlineData(4, -1)] // last item: exits
        public void Right_FlowsAcrossRows(int from, int expected) =>
            Assert.Equal(expected, GridMath.Move(from, Count, Columns, GamepadAction.NavRight));

        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 1)] // left flows to the previous row
        [InlineData(0, -1)] // first item: exits
        public void Left_FlowsAcrossRows(int from, int expected) =>
            Assert.Equal(expected, GridMath.Move(from, Count, Columns, GamepadAction.NavLeft));

        [Theory]
        [InlineData(0, 2)]
        [InlineData(1, 3)]
        [InlineData(2, 4)]
        [InlineData(3, 4)] // hole in the partial last row: land on the last item
        [InlineData(4, -1)] // last row: exits
        public void Down_HandlesPartialLastRow(int from, int expected) =>
            Assert.Equal(expected, GridMath.Move(from, Count, Columns, GamepadAction.NavDown));

        [Theory]
        [InlineData(2, 0)]
        [InlineData(4, 2)]
        [InlineData(0, -1)] // top row: exits
        [InlineData(1, -1)]
        public void Up_ExitsAtTopRow(int from, int expected) =>
            Assert.Equal(expected, GridMath.Move(from, Count, Columns, GamepadAction.NavUp));

        [Fact]
        public void InvalidInputs_ReturnMinusOne()
        {
            Assert.Equal(-1, GridMath.Move(0, 0, 2, GamepadAction.NavRight));  // empty
            Assert.Equal(-1, GridMath.Move(5, 5, 2, GamepadAction.NavRight));  // out of range
            Assert.Equal(-1, GridMath.Move(-1, 5, 2, GamepadAction.NavRight)); // negative
            Assert.Equal(-1, GridMath.Move(0, 5, 0, GamepadAction.NavRight));  // zero columns
            Assert.Equal(-1, GridMath.Move(0, 5, 2, GamepadAction.Accept));    // not directional
        }

        [Fact]
        public void SingleColumn_BehavesAsVerticalList()
        {
            Assert.Equal(1, GridMath.Move(0, 3, 1, GamepadAction.NavDown));
            Assert.Equal(0, GridMath.Move(1, 3, 1, GamepadAction.NavUp));
            Assert.Equal(1, GridMath.Move(0, 3, 1, GamepadAction.NavRight));
            Assert.Equal(-1, GridMath.Move(2, 3, 1, GamepadAction.NavDown));
        }
    }
}
