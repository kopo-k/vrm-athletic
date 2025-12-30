<?php
declare(strict_types=1);

namespace App\Test\TestCase\Model\Table;

use App\Model\Table\MessageboardTable;
use Cake\TestSuite\TestCase;

/**
 * App\Model\Table\MessageboardTable Test Case
 */
class MessageboardTableTest extends TestCase
{
    /**
     * Test subject
     *
     * @var \App\Model\Table\MessageboardTable
     */
    protected $Messageboard;

    /**
     * Fixtures
     *
     * @var array<string>
     */
    protected array $fixtures = [
        'app.Messageboard',
    ];

    /**
     * setUp method
     *
     * @return void
     */
    protected function setUp(): void
    {
        parent::setUp();
        $config = $this->getTableLocator()->exists('Messageboard') ? [] : ['className' => MessageboardTable::class];
        $this->Messageboard = $this->getTableLocator()->get('Messageboard', $config);
    }

    /**
     * tearDown method
     *
     * @return void
     */
    protected function tearDown(): void
    {
        unset($this->Messageboard);

        parent::tearDown();
    }

    /**
     * Test validationDefault method
     *
     * @return void
     * @link \App\Model\Table\MessageboardTable::validationDefault()
     */
    public function testValidationDefault(): void
    {
        $this->markTestIncomplete('Not implemented yet.');
    }
}
