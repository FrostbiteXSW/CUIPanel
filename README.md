CUI Panel （控制台面板绘制器）
====
项目目的
----
    本类可以简化在控制台窗口绘制内容的过程，提供绘制、自动刷新及按键事件处理等功能。
如何使用
----
&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;在代码中添加 `using CUIPanel` 来使用本类。<br><br>
&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;创建本类的实例，你可以自定义`UpdateRate`（更新频率，毫秒计），`UsePassiveUpdate`（使用被动更新，仅当缓冲区内容发生变化时重绘面板内容），其余属性根据需要进行修改。<br><br>
&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;初始化缓冲区内容过程中，可以置`IsPaused`属性为`true`来暂停一切更新事务，此时面板不会进行任何刷新，直到`IsPaused`属性重新置为`false`。初始化过程中可以对事件进行绑定，具体如下：
* `BeforeUpdate`：更新前事件，在每个周期的面板刷新事件发生前调用，应该包含需要预处理的事件；
* `AfterUpdate`：更新后事件，在每个周期的面板刷新事件发生后调用，应该包含需要后处理的事件；
* `AfterResize`：控制台窗口大小改变事件，在控制台窗口大小改变引发缓冲区重新初始化后调用，应该包含窗口大小改变后需要进行的操作的事件；
* `KeyPressed`：按键事件，在控制台窗口中按下某一按键后引发的事件，如果在`Console`类中设置了`CancelKeyPress`事件的处理方法，则会优先调用`Console`类中的方法。<br><br>

&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;初始化完成后即可以通过`DrawPanel`的各种重载方法绘制界面。<br><br>
&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;如果需要清空缓冲区内容，可以通过调用`Clear`方法；如果需要结束此实例，可以通过调用`Exit`方法，此方法会销毁所有从属于此实例的子线程。
