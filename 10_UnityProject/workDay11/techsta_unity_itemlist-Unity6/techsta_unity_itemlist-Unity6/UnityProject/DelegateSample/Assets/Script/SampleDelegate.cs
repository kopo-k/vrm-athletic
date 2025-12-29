using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SampleDelegate : MonoBehaviour
{
    [SerializeField] private Text _showsampletext;
    [SerializeField] private Button _LambdaBtn;
    [SerializeField] private Button _addlistenbtn;

    public delegate int Sampledelegate(int samplenum1, int samplenum2);

    void Start()
    {
        _showsampletext.text = "";

        _LambdaBtn.onClick.AddListener(() => { _showsampletext.text = "OnClickButtonLambda"; });
        _addlistenbtn.onClick.AddListener(OnClick);

        Sampledelegate sampledel;        //Delegateの変数を指定する。

        sampledel = Plus;                //Delegateに加算を指定
        Calculator(37, 12, sampledel);   //計算
        sampledel = Sub;                 //delegateに減算を指定
        Calculator(37, 12, sampledel);   //計算
    }

    /// <summary>
    /// 数字を二つ入れて加算
    /// </summary>
    /// <param name="number01">加算する数字1番</param>
    /// <param name="number02">加算する数字2番</param>
    /// <returns></returns>
    public int Plus(int number01, int number02)
    {
        return number01 + number02; //数字1番と2番を加算してReturnする。
    }

    /// <summary>
    /// 数字を二つ入れて減算
    /// </summary>
    /// <param name="number01">減算する数字1番</param>
    /// <param name="number02">減算する数字2番</param>
    /// <returns></returns>
    public int Sub(int number01, int number02)
    {
        return number01 - number02;　//数字1番と2番を減算してReturnします。
    }
    /// <summary>
    /// 計算
    /// </summary>
    /// <param name="firstnum">計算する数字1番</param>
    /// <param name="secondnum">計算する数字2番</param>
    /// <param name="calcFunction">数字を受けて計算する関数と繋がっているDelegate</param>
    public void Calculator(int firstnum, int secondnum, Sampledelegate calcFunction)
    {
        _showsampletext.text += "Result = " + calcFunction(firstnum, secondnum) + "\n";
        // 受け取った数字を計算してTextに表示します。
    }

    void OnClick()
    {
        _showsampletext.text = "OnClickButton";
    }
}
