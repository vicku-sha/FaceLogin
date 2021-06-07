using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FaceLogin
{
    public partial class InputBoxWithImage : Form
    {
        // the InputBox
        private static InputBoxWithImage newInputBox;

        // строка, которая будет возвращена форме запроса
        private static string returnString;

        public InputBoxWithImage()
        {
            InitializeComponent();
        }
        public static string Show(string inputBoxText, Image image)
        {
            newInputBox = new InputBoxWithImage();
            newInputBox.pictureBox1.Image = image;
            newInputBox.labelCap.Text = inputBoxText;
            newInputBox.ShowDialog();
            return returnString;
        }

        private void ButtonOk_Click(object sender, EventArgs e)
        {
            returnString = textBox1.Text;
            newInputBox.Dispose();
        }

        private void ButtonCancel_Click(object sender, EventArgs e)
        {
            returnString = string.Empty;
            newInputBox.Dispose();
        }
    }
}
